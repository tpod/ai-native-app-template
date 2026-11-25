---
name: tdd
description: Use when writing tests for a new or changed action or query, before the implementation. Covers the {ActionClass}Tests class, [Covers] coverage and the test helpers. Template-specific delta on the kit's tdd skill.
---

# Test-driven actions

Base: `dotnet-claude-kit:tdd`. Differences in this template:

- **The unit of test is an action**, driven over HTTP through `WebApplicationFactory`, with the real pipeline, validator, EF mapping and migrations on in-memory SQLite. No EF `InMemory` provider. A test that checks provider behaviour (migrations, collations, locking, concurrency) goes in `tests/AppTemplate.Api.PostgresTests` instead, on Testcontainers Postgres (AGENTS.md, "Databases"); action tests and `[Covers]` stay in the fast suite.
- **Side effects arrive through the outbox.** The worker is off in tests: call `await DrainOutboxAsync()` (on `ApiTestBase`) before asserting that a handler or publisher ran, and advance `Factory.Time` to reach a retry. Register a test subscriber with `services.AddEventHandler<T>(ServiceLifetime.Singleton)` in `ConfigureServices`; it needs `[Subscriber("name")]` and must be idempotent, as delivery is at least once.
- **Shouldly only.** No `Assert.*`, no FluentAssertions.
- **`[Covers]` is mandatory.** `CoverageTests` (`tests/AppTemplate.Architecture.Tests/CoverageTests.cs`) fails unless each action has a class named `{ActionClass}Tests` with `[Covers]` for `Case.HappyPath`, every declared error code and, for mutations, `Case.Stale`.
- **Microsoft.Testing.Platform commands.** Exit code 8 means no test matched the filter: treat it as a failure.

## Loop

1. Write `tests/AppTemplate.Api.Tests/Features/<Aggregate>/<ActionClass>Tests.cs` with one test per required case (list below).
2. Run it and watch it fail for the right reason. Before the action exists, every call returns `404 request.not_found`:
   `dotnet test --project tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~RenameProjectTests"`. Check `$?`: 0 is pass, 2 is failures, 8 is "no tests matched".
3. Implement in order: domain (VO, behavior, errors), then the validator, then the action class (`skill:add-action`). Rerun after each.
4. Green: run the class again, then `dotnet test --project tests/AppTemplate.Architecture.Tests` (coverage, error codes, layering).
5. `scripts/verify.sh` must print `VERIFY OK`.

## The test class

Worked example: `tests/AppTemplate.Api.Tests/Features/Projects/RenameProjectTests.cs`, with its base `tests/AppTemplate.Api.Tests/Features/Projects/ProjectTestBase.cs`.

```csharp
public sealed class RenameProjectTests : ProjectTestBase
{
    [Fact]
    [Covers("rename_project", Case.HappyPath)]
    public async Task PreviewAndExecute_NewName_RenamesProject()
    {
        var id = await CreateProject("Alpha");

        var (p, e) = await PreviewAndExecute("rename_project", new { projectId = id, name = "Beta" });

        p.ShouldSucceed();
        p.Body.GetProperty("summary").GetString().ShouldBe("Rename project 'Alpha' to 'Beta'.");
        e.ShouldSucceed();
        ResultOf(e).GetProperty("name").GetString().ShouldBe("Beta");
    }
}
```

Required cases for an action:

| Case | `[Covers]` | Typical test |
|---|---|---|
| Happy path | `[Covers("x", Case.HappyPath)]` | `PreviewAndExecute`; assert `summary`, `changes`, `sideEffects` and the execute `result`; check `version` moved by 1 |
| Each domain error | `[Covers("x", "project.name_taken")]` | Arrange the state, `Preview`, then `ShouldFailWith(code, status)`; assert key `details` |
| `validation.failed` (when a validator exists) | `[Covers("x", "validation.failed")]` | `Preview` with bad input; `ShouldHaveFieldError("name", "project_name.empty")` for each field |
| Stale (mutations) | `[Covers("x", Case.Stale)]` | `Preview`, change the aggregate through another action, `Execute` with the old token; `ShouldFailWith("preview.stale", HttpStatusCode.Conflict)` and `details.reason` |
| No-op (if the action can be one) | `Case.HappyPath` | Preview shows empty `changes`; execute leaves `version` unchanged |

A test can carry several `[Covers]` attributes. A query needs `Case.HappyPath` and its error codes only.

`Case` is our enum. `tests/AppTemplate.Api.Tests/GlobalUsings.cs` aliases it globally, because Shouldly also has a `Case` type.

## Helpers

From `tests/AppTemplate.Api.Tests/Infrastructure/ApiTestBase.cs`:

- `Preview(action, input)`, `Execute(action, input, token)`, `PreviewAndExecute(action, input)` (skips execute and returns the preview twice when the preview fails), `Query(action, input)`.
- `TokenOf(preview)`, and `IdFromChanges(preview, "TaskItem")` for the id of an added entity.
- Inputs are anonymous objects serialized as camelCase JSON: `new { projectId = id, name = "Beta" }`.

From `tests/AppTemplate.Api.Tests/Infrastructure/ApiResponseShouldly.cs`:

- `ShouldSucceed()`: status 2xx.
- `ShouldFailWith(code, HttpStatusCode.X)`: problem+json, `code`, `status`, `details`, a non-blank `suggestion`, and `traceId` equal to the `X-Trace-Id` header.
- `ShouldHaveFieldError(field, code)`: `validation.failed` with that entry in `details.fields`.

Per-aggregate base class: one per aggregate, like `ProjectTestBase`. It overrides `CreateFactory() => new(includeFixtures: false)` so only the real app's actions are registered, and holds arrange steps that run real actions (`CreateProject`, `AddTask`, `ArchiveProject`) plus `ResultOf` and `StaleReasonOf`. Arrange through actions, not by writing to the database, so arrange and act use the same path.

`ApiFactory` gives every test its own in-memory SQLite database, the fixed signing key `test-signing-key-0123456789abcdef-0123` and a `FakeTimeProvider` (`Factory.Time`). Use `Factory.Time.Advance(TimeSpan.FromSeconds(301))` to test `preview.expired`.

## Rules

- Name tests `Method_State_Expected`: `Preview_ArchivedProject_ReturnsArchived`.
- AAA with blank lines between Arrange, Act and Assert.
- Pass `TestContext.Current.CancellationToken` wherever analyzer xUnit1051 asks; warnings are errors.
- Assert a collection's size before looping over it. An empty loop passes vacuously.
- Domain unit tests (VO rules, behavior edge cases) go in `tests/AppTemplate.Api.Tests/Domain/`, like `tests/AppTemplate.Api.Tests/Domain/ProjectNameTests.cs`. They supplement the action tests; they do not count for `[Covers]`.
- Framework tests (`Actions/`, `Mcp/`, `OpenApi/`, `Telemetry/`, `Http/`, `Infrastructure/`) use only the `fixture_*` Note actions in `tests/AppTemplate.Api.Tests/Fixtures/Notes/` and never reference a real feature.
- When a test fails, read its output first: `ApiTestBase` writes each request's spans and response body there, and host logs go there too (`skill:investigate`).
