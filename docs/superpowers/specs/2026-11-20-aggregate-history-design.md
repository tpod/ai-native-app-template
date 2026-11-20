# Aggregate history: design

Date: 2026-11-20. Status: draft for approval.

## Intent

Every committed mutation leaves a durable, append-only record of the business events it raised. Any agent can read that record through one generic query.

Two readers, both agents:

1. **Runtime agents taking over work.** Agent B opens a project that agent A changed yesterday. One `get_history` call tells B what happened, when, through which action, and why A says it did it. B doesn't have to reverse-engineer this from current state.
2. **Coding agents debugging or building features.** An agent starts from an aggregate id and sees the ordered business events. Each row carries the `traceId` that links to the span and logs while the trace is still retained. The row is the durable anchor; the trace expires. Agents building features can read real event sequences to see how an aggregate is used.

The template already has most of the parts. Aggregates raise business-meaningful domain events (`Raise(new ProjectRenamed(...))` in `src/AppTemplate.Domain/Projects/Project.cs`). `ActionPipeline.ReadChangeSet` collects them from every tracked root and puts them in the preview's `sideEffects` and the change-set hash. Execute then dispatches them in-process after commit. Nothing stores them. This design stores them.

## Goals

- Persist every domain event from a committed execute as a history row **in the same transaction** as the aggregate change.
- Make history a **complete** record of committed changes: a mutation that changes state without raising an event is a programming error and commits nothing (decided by the user, 2026-11-20).
- Give each event a stable, versioned name that survives C# renames, enforced like `[Error]` codes.
- Let callers attach an optional free-text `reason` to any execute, stored on that execute's rows.
- Record which action, channel, `traceId` and, where available, MCP client produced each row.
- Expose history through one generic `get_history` query, available as an MCP tool, an HTTP query and in OpenAPI.
- Make "every state change goes through an action" a written rule, enforced by an architecture test.
- Give in-process code (imports, scheduled jobs, admin tasks) a supported way to run an action: `ActionRunner`.

## Non-goals

- **An in-app LLM "explain" engine.** The callers are already LLM agents. They explain from `get_history` output, citing the row ids as evidence. The app takes no LLM dependency.
- **A separate "business activity" concept or recording API.** In this template, domain events and history entries match one-to-one. A second API next to `Raise` would duplicate it.
- **Generic property-level change tracking** (`MonthlyAmount: 500 → 400`) as history. The preview's `changes` already shows that; history holds business events only.
- **History of failures.** Previews, stale executes, validation failures and domain errors stay in telemetry. History holds what actually happened to the aggregate.
- **A cross-aggregate timeline.** History is per aggregate in v1. A correlation key can come later.
- **Authenticated user attribution.** Auth is deferred in v1, so there is no user to record. Channel, client and `reason` are what v1 attributes.
- **PII erasure and retention.** Rows are kept forever and never edited. See Risks.
- **UI.** The web client sends no `reason` and shows no history in v1.
- **Durable event delivery or an outbox.** Dispatch to `IDomainEventHandler` stays exactly as it is.

## Anti-goals

These would make the feature a failure even if it technically works:

- **A history row without the aggregate change, or the reverse.** Atomicity is the point.
- **A history row for a preview.** A preview saves nothing; that rule stays absolute.
- **Rows that lie.** A history entry must be exactly the event the domain raised. Nothing the pipeline infers gets presented as a domain fact. `reason` is clearly labelled as caller-supplied.
- **Contract churn.** Adding `reason` must not change any action's own input record, its preview token binding or its declared error codes.
- **A silent bypass.** A state change that skips history must fail a test, not pass unnoticed.

## Constraints

- Golden rules in `AGENTS.md` hold: OneOf only, no hand-written endpoints or MCP tools, no provider-specific SQL outside `src/AppTemplate.Api/Data`, validation never throws.
- The Domain project references only OneOf. The new attribute lives in `src/AppTemplate.Domain/Common` and is plain .NET.
- SQLite in v1, with real migrations in tests. The schema must also work on Postgres (`skill:swap-to-postgres`).
- Framework tests use only the `fixture_*` Note actions. Note events (`NoteCreated`, `NoteEdited`) need the new attribute too.
- The MCP transport is stateless (`McpSetup.cs`, `o.Stateless = true`). The client name from `initialize` may not be available on later `tools/call` requests. See "Left to the builder".

## Approaches considered

**A. The pipeline writes history rows during execute.** Chosen. After the change-set hash check passes, and after the version bump, `ExecuteCoreAsync` adds one row per collected event to the same `DbContext`, then calls `SaveChangesAsync` once.
- For: every input is already in hand there (events with their owning root, action name, channel, `reason`, `traceId`, `TimeProvider`). It is one visible place, and failures land on the execute span where `skill:investigate` looks.
- Against: a write that bypasses the pipeline records nothing. The "everything through an action" rule plus its test (below) closes that gap.

**B. An EF `SaveChangesInterceptor`.** Rejected.
- `ReadChangeSet` clears each root's `DomainEvents` before `SaveChanges`, so the interceptor would find empty lists unless the pipeline changes anyway.
- The action name, `reason` and channel exist only in the pipeline. They would reach the interceptor through a hidden scoped "current call" object.
- Its extra coverage (writes outside actions) is ruled out by the rule below.
- If writes outside actions are ever allowed, recording can move into an interceptor without changing the table or the query.

**C. An `IDomainEventHandler` that writes after commit.** Rejected. It isn't atomic: a crash between the commit and the handler loses history, and the design has no outbox.

## The design

### 1. Stable event names: `[Event]`

`src/AppTemplate.Domain/Common/EventAttribute.cs`, modelled on `ErrorAttribute`:

```csharp
[Event("project.renamed", 1)]
public sealed record ProjectRenamed(Guid ProjectId, string OldName, string NewName) : IDomainEvent;
```

- `Name` matches `^[a-z_]+\.[a-z_]+$` and is unique across the Domain assembly. By convention it is `<aggregate>.<past_tense_verb>`.
- `Version` is an int, starting at 1. Bump it when the payload shape changes incompatibly, so old rows keep their old version and readers can tell them apart.
- An architecture test (next to `ErrorCodes_AreUniqueAndWellFormed`) fails unless every concrete `IDomainEvent` in the Domain assembly has `[Event]` with a well-formed, unique name.
- At runtime, an event raised without `[Event]` is a programming error. The pipeline throws, so the execute becomes `internal.unexpected` and nothing is committed. This covers test-assembly events that the architecture test doesn't scan.
- The five Project events and the two Note fixture events get attributes.
- The preview's `sideEffects[].event` keeps the CLR type name for now; switching it to the stable name is a contract change and out of scope. (Builder may raise this as a follow-up.)

### 2. The history table

An EF entity in `src/AppTemplate.Api/Data`, not a domain type: it's infrastructure, not an aggregate, and it has no `Version`. Working names: `HistoryEntry`, table `History`.

| Column | Type | Notes |
|---|---|---|
| `Id` | long, store-generated | Global, monotonic order. Agents cite it as evidence. EF `ValueGeneratedOnAdd`, no provider SQL. |
| `AggregateType` | string | CLR name of the owning root (`Project`). |
| `AggregateId` | string | `ChangeSetReader.KeyOf` of the owning root. Strongly typed ids stay out of the row. |
| `AggregateVersion` | int | The owning root's `Version` after this execute. |
| `EventType` | string | `[Event]` name, e.g. `project.renamed`. |
| `EventVersion` | int | `[Event]` version. |
| `Payload` | string (JSON) | The event serialized with the pipeline's `Json` options, exactly as for `sideEffects`. |
| `OccurredAt` | `DateTimeOffset` | `TimeProvider.GetUtcNow()` at commit; the same value for every row of one execute. |
| `ExecutionId` | Guid | One per execute; groups the rows one call produced. |
| `Action` | string | Action name, e.g. `rename_project`. |
| `Channel` | string | `http`, `mcp` or `in_process`. |
| `Client` | string? | MCP client name when known; otherwise null. |
| `Reason` | string? | Caller-supplied; see §3. |
| `TraceId` | string | `Activity.Current.TraceId` of the execute span. |

Index on `(AggregateType, AggregateId, Id)`. Events raised by child entities (`TaskAdded`) belong to their root, because the pipeline collects events per tracked root.

Append-only: no code path updates or deletes rows. The architecture rule in §6 enforces this indirectly, because only the pipeline saves.

Migration: one `AddHistory` migration via the command in `AGENTS.md`.

### 3. Optional `reason` on every execute

- `reason` is an **envelope field** beside `previewToken`. It is not part of any action's input record. It is optional, a string of at most 500 characters after trimming, and empty or whitespace counts as absent.
- It is accepted on **execute only**. It is not part of the preview token or the input hash, so a confirmed preview can be executed with any reason.
- Over-long values return `request.malformed`, with a suggestion to shorten the reason to 500 characters. It's a framework code, so no action's declared error codes change and `CoverageTests` is unaffected.
- **MCP:** `McpToolFactory` adds an optional `reason` property to every execute tool's input schema. The description is agent-facing: "Optional. One or two sentences on why you are making this change. Stored in this aggregate's history, where the next agent will read it." `McpInstructions` and `ToolDescriptions` mention it once.
- **HTTP/OpenAPI:** `OpenApiSetup` adds `reason` to each execute body schema, not required. Then `scripts/update-openapi.sh` and `npm run gen:api` regenerate the client. The web UI does not send it in v1.
- Input parsing already ignores `reason`: no `UnmappedMemberHandling` is set, so unknown body fields are dropped, the same way `previewToken` is. MCP passes tool arguments through as the body.
- `reason` becomes a **reserved input field name**, like the `preview_` prefix (my call). The action registry rejects at startup any input record with a `reason` property (case-insensitive), so it can never collide with the envelope field. No input record has one today.
- Under `skill:change-action-contract` this is an additive, optional change to every execute contract. It needs no versioning, but it gets the OpenAPI and client regeneration.

### 4. Recording in the pipeline

In `ExecuteCoreAsync`:

1. `ReadChangeSet` returns each event **together with its owning root's entry**, so type and key are known. Today it returns a flat list.
2. After the change-set hash check passes and `IncrementVersion()` has run, the pipeline adds one `HistoryEntry` per event to `db`, in raise order.
3. One `SaveChangesAsync` commits the aggregate and its history together. The existing stale (`DbUpdateConcurrencyException`) and already-executed (`DbUpdateException`) handling covers both.
4. Dispatch to `IDomainEventHandler` runs after commit, unchanged.

The hash, `changes` and `sideEffects` are all computed before the rows are added, so history rows never appear in a preview or an execute response.

**Every state change raises an event (user decision).** If `ReadChangeSet` finds `changes.Count > 0` but no events, the pipeline throws `InvalidOperationException`, naming the action and the changed aggregate. The call becomes `internal.unexpected` and nothing is committed. The check runs in **preview as well as execute** (my call): the author sees it on the first preview, not only on execute. The same applies to a missing `[Event]`.

A no-op execute (`changes.Count == 0`) raises no events and writes no rows. If a domain method raises an event without changing state, the pipeline still records it (my call). Today that path skips `SaveChanges` entirely, so the save condition becomes "changes or events". `Version` is bumped only when there are changes, as today.

The pipeline methods gain a small caller descriptor (channel, client), passed by `ActionEndpoints` (`http`), `McpSetup.CallToolAsync` (`mcp`) and `ActionRunner` (`in_process`). `reason` is read from the body like `previewToken`.

### 5. `get_history` query

A normal `[Query("get_history", ...)]` class, so the registry generates the MCP tool, the HTTP query and the OpenAPI operation. Location: `src/AppTemplate.Api/Features/History/GetHistory.cs`. That keeps it a feature, not part of the framework, and `skill:remove-example` leaves it in place.

- **Input:** `aggregateType` (string, e.g. `Project`), `aggregateId` (string, e.g. the project's Guid), optional `afterId` (long, for paging), optional `limit` (default 50, max 200).
- **Output:** a list of `{ id, eventType, eventVersion, occurredAt, aggregateVersion, action, channel, client, reason, traceId, executionId, payload }`, oldest first. `payload` is returned as a JSON object, not a string. Over MCP it arrives as `{ "items": [...] }`, as for every list result.
- **Unknown aggregate type:** `history.unknown_aggregate_type` (BadRequest). The suggestion lists the valid type names, taken from the EF model's aggregate roots.
- **No rows:** an empty list, not an error. A new aggregate created before this feature legitimately has no history.
- **Description:** aimed at runtime agents taking over work: what it returns, that `reason` is caller-supplied and not a domain fact, and that `traceId` leads to telemetry only while traces are retained.
- The validator covers `limit` range and non-empty fields. `GetHistoryTests` has `[Covers]` for HappyPath, `validation.failed` and `history.unknown_aggregate_type`.

### 6. Rule: every state change goes through an action

New golden rule 10 in `AGENTS.md`, mirrored in `.claude/rules/architecture.md`:

> Every state change goes through an action. Imports, scheduled jobs and admin tasks call an action through `ActionRunner`. They never change aggregates or call `SaveChanges` themselves. This is what keeps history, validation, stale checks and telemetry complete.

Enforced by `StateChanges_GoThroughActionPipeline` in `tests/AppTemplate.Architecture.Tests`. It uses Mono.Cecil, which NetArchTest already ships (the builder confirms it's reachable from the test project, or adds the package centrally). It scans the IL of `AppTemplate.Api`, excluding `Data/Migrations` and the design-time factory, and fails on any call outside `ActionPipeline` to:

- `DbContext.SaveChanges` / `SaveChangesAsync`
- `ExecuteUpdate` / `ExecuteUpdateAsync` / `ExecuteDelete` / `ExecuteDeleteAsync` (they skip the change tracker, and so skip events)
- `ExecuteSql*` / `ExecuteSqlRaw*` / `ExecuteSqlInterpolated*` on `DatabaseFacade`

The test asserts that it found exactly one allowed `SaveChangesAsync` call site, so an empty scan can't pass vacuously. The rule notes that any new project containing state-changing code (for example a console app for admin tasks) must be added to the test's assembly list.

### 7. `ActionRunner`: actions from in-process code

`src/AppTemplate.Api/Actions/ActionRunner.cs`, registered in DI:

```csharp
Task<OneOf<ExecuteResponse, AppError>> RunAsync(string action, object input, string? reason, CancellationToken ct);
```

- It serializes `input` with the pipeline's `Json` options, runs `PreviewAsync`, then immediately runs `ExecuteAsync` with the returned token and `reason`, using channel `in_process`. It goes through the same code path as HTTP and MCP: validation, stale checks, telemetry, history.
- It skips human or agent confirmation by design: the calling code is the trusted decision-maker. The XML doc says so.
- It returns preview errors unchanged. It does not retry on `preview.stale`; the caller decides.
- Unknown action names return the existing `request.not_found`-style error the registry uses.
- It is tested with the Note fixture: a run writes the same history rows as an HTTP execute, with `channel = in_process`.

### 8. Docs and skills

- `AGENTS.md`: golden rule 10; a short "History" section (table, `reason`, `get_history`, `[Event]`); the architecture map gains `Features/History`; the Preview protocol section mentions `reason`; the Observability section notes history as the durable anchor before `traceId`.
- `skill:domain-modeling` and `skill:add-action`: every state-changing domain method raises an event; every event gets `[Event("<aggregate>.<verb>", 1)]`; when to bump the version.
- `AGENTS.md` golden rule 8 ("Behavior lives on the aggregate") gains: "Every method that changes state raises a domain event."
- `skill:investigate`: when an aggregate behaves oddly, start with `get_history`, then follow each row's `traceId`.
- `skill:change-action-contract`: renaming an event's `[Event]` name or changing its payload shape is a history contract change. Bump the version; never rename.
- `skill:add-aggregate`: new roots get history automatically; their events need `[Event]`.
- `SkillLintTests` must stay green (referenced paths exist).

### 9. Tests

Framework tests use the Note fixture only (`tests/AppTemplate.Api.Tests/Actions/` or a new `History/` folder):

- Execute writes one row per event, in raise order, in the same commit, with every column filled as specified.
- A preview writes no rows.
- A stale execute (`version_changed`, `changes_differ`, `concurrent_write`) writes no rows.
- A no-op execute writes no rows.
- `reason` round-trips; whitespace is stored as null; 501 characters returns `request.malformed`.
- The MCP execute tool schema contains optional `reason`; the preview tool schema does not.
- The channel is `http`, `mcp` or `in_process` for each entry point.
- An event without `[Event]` fails the execute with `internal.unexpected` and commits nothing. This needs a fixture event without the attribute, and the fixture already tolerates deliberately invalid types.
- A state change with no event fails preview and execute with `internal.unexpected` and commits nothing. This needs a fixture mutation that changes state without raising, such as a new `fixture_` action, registered only for this test.
- An input record with a `reason` property fails registration.

Fixture change: `Note.Lock()` (`tests/AppTemplate.Api.Tests/Fixtures/Notes/Note.cs`) changes state without raising. It gains a `NoteLockApplied` event with `[Event("note.locked", 1)]`; `NoteLocked` is already taken by the error record. Existing `lock_note` tests that assert `sideEffects` must be updated.

Feature tests: `GetHistoryTests` (above), plus one Project-level test that renames a project and reads `project.renamed` back through `get_history`.

Architecture tests: `[Event]` presence, format and uniqueness, and `StateChanges_GoThroughActionPipeline`.

`scripts/verify.sh` prints `VERIFY OK`.

## Risks

- **PII in an append-only log.** Event payloads hold names and titles, and `reason` is free text. Deleting a project leaves its history. That's acceptable for the template, but any product with personal data needs an erasure strategy (crypto-shredding or payload redaction) before production. Recorded as a follow-up in `docs/superpowers/2026-11-20-template-v1-followups.md`.
- **Unbounded growth.** No retention in v1. The `(AggregateType, AggregateId, Id)` index keeps per-aggregate reads cheap.
- **`AggregateType` is a CLR name.** Renaming an aggregate class orphans its history without a data migration. Noted in `skill:change-action-contract`. (Builder's call: a stable aggregate name attribute is deferred.)
- **Agents may write misleading reasons.** `reason` is labelled caller-supplied everywhere it appears.

## Left to the builder

- Exact type, file and table names (`HistoryEntry`, `History`), within the conventions above.
- How the MCP client name is obtained under the stateless transport. Check the installed `ModelContextProtocol.Core` 2.4.0 for what `RequestContext` exposes; if nothing reliable exists, record null and say so in the PR.
- The caller-descriptor shape passed into the pipeline.
- Whether Mono.Cecil comes in transitively or needs a central package entry.
- Whether `get_history` validates `aggregateId` format per aggregate type, or treats it as an opaque string.
- Task order and commit boundaries.
