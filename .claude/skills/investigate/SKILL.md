---
name: investigate
description: Use when an action, query, request or test fails or behaves unexpectedly and the cause is not obvious. Debugging order is error code, traceId, trace, logs, then the failing test's captured spans and logs.
---

# Investigate a failure

Work in this order and stop as soon as the cause is clear. Each step narrows the next one. Do not change code until you can name the cause.

**No error, but an aggregate is in an unexpected state?** Start with its history: `get_history` with `aggregateType` (e.g. `Project`) and `aggregateId`. Each row shows which action changed it, when, through which channel, the caller's `reason` and the `traceId`. The rows are durable; the trace behind a `traceId` exists only while telemetry is retained. Then continue at step 2 with that `traceId`. A state change missing from history means it bypassed the pipeline, which `StateChangeTests` should have caught.

## 1. Read the error `code` (and `details`, `suggestion`)

Every error is ProblemDetails JSON, on HTTP (`application/problem+json`) and on MCP (`isError: true`), with `code`, `details`, `suggestion` and `traceId`. The code tells you which layer failed:

| Code | Layer | Where to look |
|---|---|---|
| `request.malformed` | JSON body did not bind to the input record | `details.path` is the JSON path that failed (a non-GUID string for a `Guid`, an array body, an empty body) |
| `request.not_found` | No such route or tool | The action name is wrong or not registered; check the `[Action]`/`[Query]` name and that the app started without `ActionRegistrationException` |
| `validation.failed` | The input's `AbstractValidator` | `details.fields[]` lists `{ field, code, message, suggestion }`; the field code names the VO rule (`project_name.too_long`) or validator (`project_id.not_empty`) |
| `<area>.<code>` (for example `project.name_taken`) | Domain rule, returned from `Load`/`Apply`/`Run` | The `AppError` record with that code in `src/AppTemplate.Domain/`; `git grep -n '"project.name_taken"'` finds it |
| `preview.token_invalid` | Token signature | Token edited or truncated, or the signing key changed (AppHost parameter `preview-signing-key`, config `Preview:SigningKey`) |
| `preview.token_mismatch` | Token was issued for another action or other input | Execute must send exactly the previewed input plus `previewToken` |
| `preview.expired` | Token older than its lifetime (300 s default) | Preview again |
| `preview.stale` | Execute no longer matches the preview | `details.reason`, below |
| `preview.already_executed` | A create token was executed twice | Not a bug in the caller's data; query to confirm the first execute |
| `internal.unexpected` | An exception | The detail is hidden on purpose; go to the logs (step 4) with the `traceId` |

`preview.stale` `details.reason` (also the span tag `preview.stale_reason`), set in `src/AppTemplate.Api/Actions/ActionPipeline.cs`:

| Reason | Meaning | Usual cause |
|---|---|---|
| `version_changed` | The aggregate's `Version` moved since the preview | Someone else changed it; preview again. If nobody did, check that `Version` is a concurrency token and not modified by your code |
| `domain_error` | `Load` or `Apply` now returns an error | `details.cause` holds `{ code, message }` of that error |
| `changes_differ` | Same versions, but `Apply` produced a different change set, side effects or warnings | Non-determinism in the handler or domain: `DateTime.UtcNow`, `Guid.NewGuid()`, unordered collections. Use `ctx.Time` and `ctx.NewId()` |
| `concurrent_write` | `SaveChanges` threw `DbUpdateConcurrencyException` | A real race between two executes; preview again |

## 2. Take the `traceId`

The body's `traceId` equals the `X-Trace-Id` response header, which every HTTP response carries, including successes. In the UI, `<ProblemAlert>` shows it as "trace …". For a successful but wrong result, read the header: `curl -si -X POST http://localhost:5180/api/queries/list_projects -H 'Content-Type: application/json' -d '{}' | grep -i x-trace-id`.

## 3. Find the trace

With the AppHost running (`dotnet run --project src/AppTemplate.AppHost`):

- **Aspire MCP** (`aspire` in `.mcp.json`, which runs `aspire agent mcp`): ask it for the traces and structured logs of resource `api`, filtered by the trace id. `aspire:aspire-monitoring` covers its tools. Never run `aspire agent init`.
- **Aspire dashboard** (`http://localhost:15165`; the AppHost console prints the login link): Traces, then search for the trace id.

Each action phase is one span on ActivitySource `AppTemplate.Actions`, named `action {name} {phase}` with phase `preview`, `execute` or `query`, for example `action rename_project execute`. Tags (`src/AppTemplate.Api/Actions/Telemetry/ActionTelemetry.cs`):

| Tag | Value |
|---|---|
| `action.name` | `rename_project` |
| `action.phase` | `preview`, `execute` or `query` |
| `action.outcome` | `success`, or the error code (`project.name_taken`, `preview.stale`, `internal.unexpected`) |
| `aggregate.type` | CLR type of the loaded aggregate (`Project`); missing when `Load` failed |
| `aggregate.id` | its key |
| `preview.stale_reason` | only on `preview.stale` |

An exception sets span status `Error` and outcome `internal.unexpected`. EF Core child spans show the SQL that ran. The `AppTemplate.Actions` Meter records `app.actions.calls` and `app.actions.duration`, tagged with `action.name`, `action.phase` and `action.outcome`. Use it to see whether a failure is new or frequent.

## 4. Read the logs for that trace

- A failed phase logs one warning (`src/AppTemplate.Api/Actions/Telemetry/ActionLog.cs`): `Action {ActionName} {Phase} failed with {ErrorCode} (traceId {TraceId})`.
- An exception is logged once, with its stack trace, by the host's exception handler under the same trace id. This is the only place `internal.unexpected` details appear.
- Through the Aspire MCP or the dashboard's Structured logs page, filter by the trace id. Console logs of the `api` resource show startup failures such as `ActionRegistrationException`, which lists every registry rule violation.

## 5. Reproduce in a test and read its output

Write or run the narrowest failing test (`skill:tdd`):

```bash
dotnet test --project tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~RenameProjectTests"
```

Check `$?`: 0 is pass, 2 is test failures, 8 is "no test matched the filter" (fix the filter, it is not a pass).

`ApiTestBase` (`tests/AppTemplate.Api.Tests/Infrastructure/ApiTestBase.cs`) writes every request to the test output, which is printed when the test fails. Example (abridged; ids and timings vary):

```
POST /api/actions/rename_project -> 409
trace 4bf92f3577b34da6a3ce929d0e0e4736: 2 span(s)
  [Microsoft.AspNetCore] POST /api/actions/rename_project (12.3 ms, Unset)
  [AppTemplate.Actions] action rename_project execute (8.1 ms, Unset)
      action.name = rename_project
      action.phase = execute
      aggregate.type = Project
      aggregate.id = …
      preview.stale_reason = version_changed
      action.outcome = preview.stale
{"type":…,"code":"preview.stale",…}
```

Host logs go to the same output through `TestOutputLoggerProvider`, including exception stack traces. `ShouldFailWith` prints the response body in its failure message.

Common test-only causes:

- Exit 8 or "0 tests": the filter does not match a class or method name.
- `404 request.not_found` for a real action: the test class uses fixtures (`ApiFactory` default) while the action is real, or the name is misspelled. Feature tests derive from a base with `includeFixtures: false`.
- `PersistenceTests.Model_AfterMigrations_HasNoPendingChanges` fails: a model change has no migration (`skill:ef-core`).
- `CoverageTests` fails: a declared error code has no `[Covers]` test; its message lists `action code` pairs.
- `OpenApiSnapshotTests` fails: run `scripts/update-openapi.sh`.

## When you have the cause

Write a failing test that pins it, fix it at the narrowest layer (domain rule, validator, handler, mapping), then run `scripts/verify.sh` until it prints `VERIFY OK`.
