# Aggregate History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Store every committed domain event as an append-only history row in the same transaction, and expose it through a `get_history` query, an optional `reason` on executes, an in-process `ActionRunner`, and a history panel in the web UI.

**Architecture:** `ActionPipeline.ExecuteCoreAsync` turns the events it already collects into `HistoryEntry` rows before its single `SaveChangesAsync`. Events get stable names via `[Event]`, and a state change without an event fails. An IL-scanning architecture test makes the pipeline the only writer. `get_history` is an ordinary `[Query]`, so HTTP, MCP, OpenAPI and the TS client come from the registry.

**Tech Stack:** .NET 10, EF Core (SQLite), ModelContextProtocol 2.4.0, FluentValidation, xUnit v3 + Shouldly, NetArchTest + Mono.Cecil 0.11.6 (already transitive in the architecture tests), React + TanStack Query + shadcn/ui + lucide-react.

**Spec:** `docs/superpowers/specs/2026-11-20-aggregate-history-design.md`. Read it before any task: it holds the reasons; this plan holds the decisions.

## Global Constraints

- Golden rules in `AGENTS.md` apply. OneOf only, no hand-written endpoints or MCP tools, no provider-specific SQL outside `src/AppTemplate.Api/Data`, validation never throws, Shouldly only, test names `Method_State_Expected`, pass `TestContext.Current.CancellationToken`.
- Framework tests (`Actions/`, `Mcp/`, `OpenApi/`, `Http/`, `Infrastructure/`, new `History/`) use only `fixture_*` Note actions. Register extra fixture types through `ActionRegistryOptions.Types` in a test's `ConfigureServices`, never by scanning the test assembly.
- Event names match `^[a-z_]+\.[a-z_]+$`, are unique, and their versions start at `1`.
- `reason`: optional string, at most 500 characters after trimming. Empty or whitespace counts as absent. Too long → `request.malformed`. Accepted on execute only and not bound by the preview token.
- Channel values are exactly `http`, `mcp` and `in_process`.
- `get_history`: `limit` defaults to 50, maximum 200, and results come oldest first.
- Missing `[Event]`, or state changes without an event, throw `InvalidOperationException` → `internal.unexpected`, with nothing committed. This applies in preview and execute.
- `scripts/verify.sh` must print `VERIFY OK` at the end of every task that touches code. Exit code 8 from `dotnet test` means zero tests matched, which is a failure.
- Commit after each task with a Conventional Commit message ending `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Do not push.

## Review Focus

1. **`reason` sent as a non-string JSON value** (number, object, `true`). Expect `request.malformed` at `$.reason`. JSON `null` counts as absent. → test in Task 4.
2. **`get_history` with an upper-case or brace-less GUID `aggregateId`.** Expect the same rows as the canonical lowercase form, because agents copy ids from many places. → test in Task 6.
3. **`get_history` with `aggregateType` in a different case** (`project`). Expect the same rows as `Project`. → test in Task 6.
4. **A second execute of an already-executed create token** (`preview.already_executed`). Expect no duplicate history row. → test in Task 3.
5. **`afterId` at or past the last row.** Expect an empty list, not an error. → test in Task 6.

---

### Task 1: Stable event names with `[Event]`

**Files:**
- Create: `src/AppTemplate.Domain/Common/EventAttribute.cs`
- Modify: `src/AppTemplate.Domain/Projects/ProjectEvents.cs` (all 5 records)
- Modify: `tests/AppTemplate.Api.Tests/Fixtures/Notes/NoteEvents.cs` (both records)
- Create: `tests/AppTemplate.Architecture.Tests/EventNameTests.cs`

**Interfaces:**
- Produces: `[AttributeUsage(AttributeTargets.Class, Inherited = false)] public sealed class EventAttribute(string name, int version) : Attribute` with `string Name`, `int Version`. Same shape as `ErrorAttribute`.
- Names: `project.created`, `project.renamed`, `project.task_added`, `project.task_completed`, `project.archived`, `note.created`, `note.edited`.

- [ ] **Step 1: Write the failing tests** in `EventNameTests`, scanning the Domain assembly (`typeof(Entity<>).Assembly`), modelled on `ErrorCodeTests.ErrorCodes_AreUniqueAndWellFormed`:
  - `DomainEvents_AllHaveEventAttribute`: assert the concrete `IDomainEvent` count is `> 0` first, then that each has `[Event]`.
  - `EventNames_AreUniqueAndWellFormed`: every name matches `^[a-z_]+\.[a-z_]+$`, there are no duplicates, and every `Version >= 1`.
- [ ] **Step 2: Run** `dotnet test --project tests/AppTemplate.Architecture.Tests --filter "FullyQualifiedName~EventNameTests"`. Expected: FAIL (the attribute type doesn't exist yet → compile error).
- [ ] **Step 3: Add `EventAttribute`** and annotate the 7 events with the names above, version `1`.
- [ ] **Step 4: Run** the same command. Expected: PASS. Then run `scripts/verify.sh` and expect `VERIFY OK`.
- [ ] **Step 5: Commit** `feat(domain): give domain events stable versioned names`.

---

### Task 2: History table

**Files:**
- Create: `src/AppTemplate.Api/Data/HistoryEntry.cs`
- Create: `src/AppTemplate.Api/Data/HistoryEntryConfiguration.cs`
- Modify: `src/AppTemplate.Api/Data/AppDbContext.cs` (add `DbSet<HistoryEntry> History`)
- Create: migration `AddHistory` under `src/AppTemplate.Api/Data/Migrations`
- Test: `tests/AppTemplate.Api.Tests/Data/PersistenceTests.cs` (add one test)

**Interfaces:**
- Produces: `public sealed class HistoryEntry`, with `init` properties and a public constructor taking every column except `Id`:
  - `long Id`
  - `string AggregateType`
  - `string AggregateId`
  - `int AggregateVersion`
  - `string EventType`
  - `int EventVersion`
  - `string Payload`
  - `DateTimeOffset OccurredAt`
  - `Guid ExecutionId`
  - `string Action`
  - `string Channel`
  - `string? Client`
  - `string? Reason`
  - `string? TraceId`
- `TraceId` is nullable: there is no activity when no listener is attached. That's my call; the spec allows it.
- Table `History`. `Id` uses `ValueGeneratedOnAdd`. Index `(AggregateType, AggregateId, Id)`. `Reason` has max length 500.

- [ ] **Step 1: Write the failing test** `History_RoundTripsEveryColumn` in `PersistenceTests`. Add one `HistoryEntry` through `Factory.Services.CreateScope()` + `AppDbContext`, save it, then read it back in a new scope. Assert every column equals what was written, and that `Id > 0`.
- [ ] **Step 2: Run** `dotnet test --project tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~History_RoundTripsEveryColumn"`. Expected: FAIL (compile error).
- [ ] **Step 3: Add the entity and configuration.** Then run `dotnet tool run dotnet-ef migrations add AddHistory -p src/AppTemplate.Api -o Data/Migrations`. Check the generated migration has no provider-specific SQL outside `Data/`.
- [ ] **Step 4: Run** the test. Expected: PASS.
- [ ] **Step 5: Run** `scripts/verify.sh` and expect `VERIFY OK`. Then commit `feat(data): add append-only History table`.

---

### Task 3: Record history in execute and enforce the event rules

**Files:**
- Create: `src/AppTemplate.Api/Actions/ActionCaller.cs`
- Modify: `src/AppTemplate.Api/Actions/ActionPipeline.cs` (`ExecuteAsync`, `ExecuteCoreAsync`, `ReadChangeSet`)
- Modify: `src/AppTemplate.Api/Actions/Http/ActionEndpoints.cs:32`, `src/AppTemplate.Api/Mcp/McpSetup.cs:123`, `tests/AppTemplate.Api.Tests/Actions/PipelineTests.cs:312` (new `ExecuteAsync` argument)
- Modify: `tests/AppTemplate.Api.Tests/Fixtures/Notes/Note.cs` (`Lock` raises), `NoteEvents.cs` (add `NoteLockApplied`)
- Create: `tests/AppTemplate.Api.Tests/Fixtures/Notes/SilentNoteFixtures.cs`, holding two test-only mutations registered per test:
  - `fixture_touch_note_silently`: changes `Text` without raising any event
  - `fixture_edit_note_unnamed`: raises an event record that has no `[Event]`
- Create: `tests/AppTemplate.Api.Tests/History/HistoryRecordingTests.cs`

**Interfaces:**
- Consumes: `EventAttribute` (Task 1), `HistoryEntry` (Task 2).
- Produces:
  - `public sealed record ActionCaller(string Channel, string? Client)`, with `static ActionCaller Http`, `static ActionCaller InProcess` and `static ActionCaller Mcp(string? client)`.
  - New signature `ExecuteAsync(ActionDescriptor action, JsonElement body, ActionCaller caller, CancellationToken ct)`. `PreviewAsync` is unchanged.
  - `ReadChangeSet` returns events paired with their owner: `internal sealed record RaisedEvent(IDomainEvent Event, IAggregateRoot Root, string AggregateType, string AggregateId)`. `AggregateType` is `entry.Metadata.ClrType.Name`; `AggregateId` is `ChangeSetReader.KeyOf(entry)`.
  - `NoteLockApplied(Guid NoteId)` with `[Event("note.locked", 1)]`.
- MCP client name: look in `ModelContextProtocol.Core` 2.4.0 (`~/.nuget/packages/modelcontextprotocol.core/2.4.0`) for what `RequestContext<CallToolRequestParams>` exposes, such as `request.Server.ClientInfo`. Under `Stateless = true`, if nothing reliable exists, pass `null` and note that in the commit body.

- [ ] **Step 1: Write the failing tests** in `HistoryRecordingTests : ApiTestBase`. Read rows through a private helper `Task<List<HistoryEntry>> Rows()` that orders by `Id` in a fresh scope.
  - `Execute_CreateNote_WritesOneRowWithEveryColumn`: exactly 1 row. `EventType == "note.created"`, `EventVersion == 1`, `AggregateType == "Note"`, `AggregateId` is the note id, `AggregateVersion == 1`, `Action == "fixture_create_note"`, `Channel == "http"`. `Payload` parses as JSON with `text` equal to the input text. `OccurredAt == Factory.Time.GetUtcNow()`. `ExecutionId != Guid.Empty`.
  - `Execute_EditNote_AppendsSecondRowWithHigherVersion`: after create then edit, 2 rows. The second has `note.edited` and `AggregateVersion == 2`.
  - `Preview_Any_WritesNoRows`: a preview with no execute leaves 0 rows.
  - `Execute_Stale_WritesNoRows`: create a note (1 row). Preview edit A, then preview and execute edit B (2 rows). Executing A's token returns `preview.stale`, and the row count stays 2.
  - `Execute_AlreadyExecutedCreate_WritesNoDuplicateRow` (Review Focus 4): execute the same create token twice. The second returns `preview.already_executed`, and there is exactly 1 row.
  - `Execute_LockNote_RecordsNoteLocked`: the last row is `note.locked`.
  - `Preview_StateChangeWithoutEvent_FailsUnexpected` and `Execute_StateChangeWithoutEvent_FailsUnexpected`, using `fixture_touch_note_silently`: `ShouldFailWith("internal.unexpected", HttpStatusCode.InternalServerError)`, and the note's text is unchanged in the DB.
  - `Execute_EventWithoutAttribute_FailsUnexpected`, using `fixture_edit_note_unnamed`: same assertions.
  - `Execute_McpCall_RecordsMcpChannel`: execute through the MCP client used in `tests/AppTemplate.Api.Tests/Mcp`. The row has `Channel == "mcp"`.
- [ ] **Step 2: Run** `dotnet test --project tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~HistoryRecordingTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
  - In `ReadChangeSet`, resolve each event's `[Event]`. If one is missing, throw `InvalidOperationException($"Domain event {type} has no [Event] attribute.")`. If `changes.Count > 0 && events.Count == 0`, throw an `InvalidOperationException` naming the action and the first changed entity.
  - In `ExecuteCoreAsync`, after `IncrementVersion()` and before `SaveChangesAsync`, add one `HistoryEntry` per `RaisedEvent` in raise order:
    - `AggregateVersion = Root.Version`
    - `Payload`: serialize with `Json` exactly as `sideEffects` does
    - `OccurredAt = time.GetUtcNow()`, the same value for the whole execute
    - one `ExecutionId = Guid.CreateVersion7()` per execute
    - `TraceId = Activity.Current?.TraceId.ToString()`
  - Save when `changes.Count > 0 || events.Count > 0`. Increment `Version` only when `changes.Count > 0`.
- [ ] **Step 4: Run** the `HistoryRecordingTests` filter, then the full Api test project. Expected: PASS. Fix existing `fixture_lock_note` assertions on `sideEffects` if any fail.
- [ ] **Step 5: Commit** `feat(actions): record domain events as history in the execute transaction`.

---

### Task 4: Optional `reason` on execute

**Files:**
- Modify: `src/AppTemplate.Api/Actions/ActionPipeline.cs` (read `reason` in `ExecuteCoreAsync`)
- Modify: `src/AppTemplate.Api/Actions/ActionRegistry.cs` (reserve the name)
- Modify: `src/AppTemplate.Api/Mcp/McpToolFactory.cs:55-90`, `ToolDescriptions.cs`, `McpInstructions.cs`
- Modify: `src/AppTemplate.Api/Actions/Http/OpenApiSetup.cs:50-70`
- Modify: `tests/AppTemplate.Api.Tests/Infrastructure/ApiTestBase.cs` (add `Execute(string action, object input, string token, string? reason)`)
- Test: `tests/AppTemplate.Api.Tests/History/ReasonTests.cs`, plus a case in `tests/AppTemplate.Api.Tests/Actions/ActionRegistryTests.cs`

**Interfaces:**
- Produces:
  - `McpToolFactory.ReasonProperty = "reason"` and `ActionRegistry.ReservedInputField = "reason"`.
  - MCP description, exact copy: `Optional. One or two sentences on why you are making this change. Stored in this aggregate's history, where the next agent will read it.`
  - OpenAPI description: `Optional reason for this change, stored in the aggregate's history. At most 500 characters.`

- [ ] **Step 1: Write the failing tests.**
  - `Execute_WithReason_StoresTrimmedReason`: with reason `"  customer asked  "`, the row's `Reason` is `"customer asked"`.
  - `Execute_WhitespaceReason_StoresNull`
  - `Execute_NullReason_StoresNull`
  - `Execute_Reason500Chars_Succeeds`
  - `Execute_Reason501Chars_ReturnsMalformed`: `ShouldFailWith("request.malformed", HttpStatusCode.BadRequest)`, and there are no rows.
  - `Execute_NonStringReason_ReturnsMalformed` (Review Focus 1): reason `42` → `request.malformed`, with `details` pointing at `$.reason`. Match the existing `RequestMalformed` details shape.
  - `Preview_WithReason_TokenStillValidWithDifferentReason`: preview with no reason, then execute with a reason. It succeeds.
  - `McpTools_ExecuteSchema_HasOptionalReason` and `McpTools_PreviewSchema_HasNoReason`: `reason` appears in the execute tool's `properties` and not in `required`.
  - `OpenApi_ExecuteBody_HasOptionalReason`: in the existing OpenApi tests folder.
  - `Register_InputWithReasonProperty_Fails`: in `ActionRegistryTests`, a fixture mutation whose input record has `string Reason` produces an `ActionRegistrationException` whose message names `reason`.
- [ ] **Step 2: Run** `dotnet test --project tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~ReasonTests|FullyQualifiedName~ActionRegistryTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
  - Parse `reason` from the body before the token check. A string is trimmed, and empty becomes `null`. JSON `null` or absent becomes `null`. Any other kind, or more than 500 characters, returns `new RequestMalformed(..., "$.reason")`.
  - Set `HistoryEntry.Reason`.
  - In the registry, a case-insensitive check against the input type's public properties. The failure message mirrors the `preview_` one.
  - Add the MCP and OpenAPI properties without marking them required.
- [ ] **Step 4: Run** the same filter, then `scripts/update-openapi.sh && (cd src/web && npm run gen:api)`. Expected: tests PASS and `schema.d.ts` regenerates.
- [ ] **Step 5: Commit** `feat(actions): accept an optional reason on execute and store it in history`.

---

### Task 5: `ActionRunner` for in-process callers

**Files:**
- Create: `src/AppTemplate.Api/Actions/ActionRunner.cs`
- Modify: `src/AppTemplate.Api/Actions/ActionServiceCollectionExtensions.cs` (register it as scoped or singleton; it only needs `ActionRegistry`, `ActionPipeline` and JSON options)
- Test: `tests/AppTemplate.Api.Tests/History/ActionRunnerTests.cs`

**Interfaces:**
- Consumes: `ActionCaller.InProcess` (Task 3), the `reason` parsing (Task 4).
- Produces: `public sealed class ActionRunner` with `Task<OneOf<ExecuteResponse, AppError>> RunAsync(string action, object input, string? reason, CancellationToken ct)`.
  - Serialize `input` with the pipeline's `Json` options.
  - Preview, then execute with the token and, if `reason` is non-null, with `reason` added to the body.
  - Preview errors are returned unchanged. There is no retry on `preview.stale`.
  - An unknown name or a query name returns the same `request.not_found` error the HTTP side uses. Find its record in `FrameworkErrors.cs`.
  - The XML doc says that confirmation is skipped by design, because the calling code is the trusted decision-maker.

- [ ] **Step 1: Write the failing tests.**
  - `RunAsync_CreateNote_WritesInProcessRowWithReason`: `Channel == "in_process"`, and `Reason` is as passed.
  - `RunAsync_ValidationFailure_ReturnsValidationFailed`: empty note text → `validation.failed`, with no rows.
  - `RunAsync_UnknownAction_ReturnsNotFound`
  - `RunAsync_QueryName_ReturnsNotFound`
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~ActionRunnerTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** `ActionRunner` as specified.
- [ ] **Step 4: Run** the filter. Expected: PASS.
- [ ] **Step 5: Commit** `feat(actions): add ActionRunner so jobs and imports run actions in-process`.

---

### Task 6: `get_history` query

**Files:**
- Create: `src/AppTemplate.Api/Features/History/GetHistory.cs` (input, DTO, handler)
- Create: `src/AppTemplate.Api/Features/History/HistoryErrors.cs`
- Create: `src/AppTemplate.Api/Features/History/HistoryValidators.cs`
- Create: `tests/AppTemplate.Api.Tests/Features/History/GetHistoryTests.cs`, deriving from a base with `includeFixtures: false` like `ProjectTestBase`. It uses the Project actions, since this is a feature test.
- Modify: `src/web/src/api/schema.d.ts` (regenerated only)

**Interfaces:**
- Consumes: `HistoryEntry` (Task 2).
- Produces:
  - `public sealed record GetHistoryInput(string AggregateType, string AggregateId, long? AfterId = null, int? Limit = null)`
  - `public sealed record HistoryEntryDto(long Id, string EventType, int EventVersion, DateTimeOffset OccurredAt, int AggregateVersion, string Action, string Channel, string? Client, string? Reason, string? TraceId, Guid ExecutionId, JsonElement Payload)`
  - `[Query("get_history", "...")] public sealed class GetHistory : IQuery<GetHistoryInput, IReadOnlyList<HistoryEntryDto>>`, whose `Run` returns `OneOf<IReadOnlyList<HistoryEntryDto>, UnknownAggregateType>`.
  - `[Error("history.unknown_aggregate_type", ErrorKind.BadRequest, "Unknown aggregate type")] public sealed record UnknownAggregateType(string Given, IReadOnlyList<string> Known) : AppError`
    - Suggestion: `"Use one of: {Known joined by ', '}."`
    - Details: `new { given = Given, known = Known }`
  - Query description, exact copy: `Business history of one aggregate, oldest first: each committed event with its action, channel, time, traceId and the caller's optional reason. reason is caller-supplied, not a domain fact. traceId leads to telemetry only while traces are retained. Page with afterId.`
- Resolving names: the known types are the CLR names of EF model entity types assignable to `IAggregateRoot`, matched case-insensitively (Review Focus 3). Any `AggregateId` that `Guid.TryParse` accepts is normalized to `ToString("D")` lowercase, the format `ChangeSetReader.KeyOf` writes (Review Focus 2). Confirm that format in the Task 3 test data.
- Validator: `AggregateType` and `AggregateId` are not empty (`.WithErrorCode("history.field_required")`), and `Limit` is between 1 and 200 (`.WithErrorCode("history.limit_out_of_range")`), each with a `.WithSuggestion`.

- [ ] **Step 1: Write the failing tests**, each with `[Covers("get_history", ...)]`:
  - `Query_RenamedProject_ReturnsCreatedThenRenamed` (`Case.HappyPath`): create, then rename, then query. 2 items: `project.created`, then `project.renamed`. The second payload has `newName`.
  - `Query_UnknownType_ReturnsUnknownAggregateType` (`"history.unknown_aggregate_type"`): `ShouldFailWith(..., HttpStatusCode.BadRequest)`, and `details.known` contains `"Project"`.
  - `Query_LimitZero_ReturnsValidationFailed` (`"validation.failed"`): `ShouldHaveFieldError("limit", "history.limit_out_of_range")`.
  - `Query_LowercaseType_MatchesProject`
  - `Query_UppercaseGuid_MatchesRows`
  - `Query_AfterIdPastEnd_ReturnsEmpty`
  - `Query_Limit1_ReturnsOnlyFirst`
  - `Query_NoRows_ReturnsEmpty`
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~GetHistoryTests"`. Expected: FAIL.
- [ ] **Step 3: Implement** the query with `AsNoTracking`. Filter by type and id, plus `Id > afterId`, order by `Id`, and take `limit ?? 50`. Project `Payload` with `JsonDocument.Parse(...).RootElement.Clone()` after materializing.
- [ ] **Step 4: Run** the filter, then `scripts/update-openapi.sh && (cd src/web && npm run gen:api)` and `scripts/verify.sh`. Expected: tests PASS and `VERIFY OK`.
- [ ] **Step 5: Commit** `feat(history): add get_history query`.

---

### Task 7: Rule "every state change goes through an action"

**Files:**
- Create: `tests/AppTemplate.Architecture.Tests/StateChangeTests.cs`
- Modify: `AGENTS.md`:
  - new golden rule 10
  - rule 8 gains "Every method that changes state raises a domain event."
  - a "History" section
  - the architecture map gains `Features/History/`
  - the preview protocol mentions `reason`
  - observability puts `get_history` before `traceId`
  - the frontend rules gain the `<HistoryButton>` line
- Modify: `.claude/rules/architecture.md` (mirror rule 10)

**Interfaces:**
- Rule 10, exact copy: `Every state change goes through an action. Imports, scheduled jobs and admin tasks call an action through ActionRunner. They never change aggregates or call SaveChanges themselves. This is what keeps history, validation, stale checks and telemetry complete. A new project containing state-changing code must be added to StateChangeTests.`

- [ ] **Step 1: Write the failing test** `StateChanges_GoThroughActionPipeline`.
  - Load `typeof(ActionRegistryOptions).Assembly.Location` with `Mono.Cecil.AssemblyDefinition.ReadAssembly`.
  - Walk every method body, including nested and compiler-generated types.
  - Collect each `call`/`callvirt` whose target is one of:
    - `SaveChanges`/`SaveChangesAsync` declared on `DbContext` or a subclass
    - `ExecuteUpdate*`/`ExecuteDelete*` from `Microsoft.EntityFrameworkCore`
    - `ExecuteSql*` on `DatabaseFacade`/`RelationalDatabaseFacadeExtensions`
  - Exclude namespace `AppTemplate.Api.Data.Migrations` and type `AppDbContextDesignTimeFactory`.
  - Assert the allowed hits (the declaring top-level type is `ActionPipeline`) number exactly 1 `SaveChangesAsync`, and that the disallowed hits are empty, with each offender listed as `Type.Method`.
  - Before implementing, prove the test can fail: temporarily add a `SaveChanges` call in a throwaway Api type, run it, see it fail, then remove it.
- [ ] **Step 2: Run** `dotnet test --project tests/AppTemplate.Architecture.Tests --filter "FullyQualifiedName~StateChangeTests"`. Expected: PASS on the current code. The deliberate-failure check from Step 1 is the red phase. Report both runs.
- [ ] **Step 3: Update** `AGENTS.md` and `.claude/rules/architecture.md` as listed.
- [ ] **Step 4: Run** `scripts/verify.sh`. Expected: `VERIFY OK`.
- [ ] **Step 5: Commit** `feat(arch): require every state change to go through the action pipeline`.

---

### Task 8: History button and panel in the web UI

**Files:**
- Run from `src/web`: `npx shadcn@latest add sheet tooltip`, which creates `src/web/src/components/ui/sheet.tsx` and `tooltip.tsx`. Never hand-edit them.
- Create: `src/web/src/components/history-button.tsx`
- Create: `src/web/src/components/history-panel.tsx`
- Modify: `src/web/src/pages/project-detail-page.tsx` (beside the `<h1>` at line 66; a new column in the task `columns`)
- Modify: `src/web/src/pages/projects-page.tsx` (a new column in `columns`)
- Modify: `src/web/src/App.tsx` or `main.tsx` (wrap in `TooltipProvider` if the installed shadcn tooltip needs it; check the generated file)

**Interfaces:**
- Consumes: `runQuery('get_history', …)` and `QueryResult<'get_history'>` from `src/web/src/api/actions.ts`, and `<ProblemAlert>`.
- Produces:
  - `export function HistoryButton(props: { aggregateType: string; aggregateId: string; filter?: { key: string; value: string }; label?: string }): JSX.Element`
  - `export function HistoryPanel(...)`, used only by `HistoryButton`.
- Exact copy:
  - Tooltip and `aria-label`: `Business history`
  - Empty state: `No history recorded yet.`
  - Reason label: `Reason given`
  - Filter banner: `Showing entries for this task — show all`
  - Paging button: `Load more`
- Event label: `eventType` split at the `.`, `_` → space, sentence case (`project.task_added` → `Project task added`).
- Query key `['history', aggregateType, aggregateId]`, with `enabled` only while the sheet is open, so the existing invalidate-after-execute refreshes it. Display is newest first (reverse the oldest-first result). "Load more" fetches the next page with `afterId` set to the highest id held, then appends.
- Task rows: `<HistoryButton aggregateType="Project" aggregateId={project.id} filter={{ key: 'taskId', value: task.id }} />`. The filter compares `String(payload[key]).toLowerCase()` to `value.toLowerCase()`.

- [ ] **Step 1: Add the shadcn components** and build `HistoryButton` and `HistoryPanel` per `skill:frontend-feature`. Use the lucide `History` icon on a ghost icon `Button`. Use only Tailwind tokens and no custom CSS.
- [ ] **Step 2: Place the buttons** in the three spots.
- [ ] **Step 3: Run** `cd src/web && npm run typecheck && npm run lint && npm run build`. Expected: all exit 0.
- [ ] **Step 4: Check it by hand.** Run `dotnet run --project src/AppTemplate.AppHost`. Create a project, rename it, and add and complete a task. Then:
  - open the panel from the title: 4 entries, newest first
  - open it from the task row: 2 entries plus the filter banner
  - click "show all": 4 entries
  - confirm the open panel refreshes after an action

  Say in the commit body that the check was manual, because the web app has no component tests.
- [ ] **Step 5: Commit** `feat(web): show business history from a button on every entity`.

---

### Task 9: Skills, follow-ups, final verification

**Files:**
- Modify the following under `.claude/skills/`:
  - `domain-modeling/SKILL.md`: every state-changing method raises an event; `[Event("<aggregate>.<verb>", 1)]`; when to bump the version.
  - `add-action/SKILL.md`: the same event rule, plus that `reason` is reserved.
  - `add-aggregate/SKILL.md`: history is automatic; events need `[Event]`; add `<HistoryButton>` to its pages.
  - `investigate/SKILL.md`: start with `get_history`, then follow `traceId`.
  - `change-action-contract/SKILL.md`: changing an `[Event]` name or payload is a history contract change; bump the version and never rename.
  - `frontend-feature/SKILL.md`: every page showing an aggregate or child entity has a `<HistoryButton>`.
- Modify: `docs/superpowers/2026-11-20-template-v1-followups.md`:
  - PII erasure for history
  - retention
  - a stable aggregate-type name
  - a server-side entity filter for `get_history`
  - switching preview `sideEffects[].event` to the stable name
  - the MCP client name, if it was recorded as null
- Modify: `README.md`: drop "(planned)" from the two history rows.

- [ ] **Step 1: Edit** the files listed. Use only paths that exist, because `SkillLintTests.RepoSkills_ReferencedPathsExist` checks them.
- [ ] **Step 2: Run** `scripts/verify.sh`. Expected: `VERIFY OK`.
- [ ] **Step 3: Commit** `docs: teach skills and AGENTS.md about aggregate history`.
