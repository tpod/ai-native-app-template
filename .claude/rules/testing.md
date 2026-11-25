---
alwaysApply: true
description: >
  Enforces testing strategy, patterns, and naming conventions for this
  template: xUnit v3 on Microsoft.Testing.Platform, WebApplicationFactory,
  in-memory SQLite with real migrations (Testcontainers Postgres only for
  provider behaviour), Shouldly, and [Covers] coverage.
---

> Edited for this template: differs from dotnet-claude-kit on the test database (in-memory SQLite with real migrations for the fast suite; Testcontainers Postgres only for provider behaviour), assertions (Shouldly, not `Assert`), fixtures (one database per test) and mandatory `[Covers]` coverage per action. See AGENTS.md → dotnet-claude-kit overrides.
> Source: [dotnet-claude-kit](https://github.com/codewithmukesh/dotnet-claude-kit) `.claude/rules/testing.md`, commit 6de62a5 (kit v0.14.0). MIT License, Copyright (c) 2025 Mukesh Murugan.

# Testing Rules

## Strategy

- **Integration tests first.** Use `WebApplicationFactory` (`ApiFactory`, via `ApiTestBase` in `tests/AppTemplate.Api.Tests/Infrastructure`) to test real HTTP pipelines. Integration tests catch the bugs that unit tests miss: serialization, middleware, DI wiring, and query behavior.
- **In-memory SQLite with real migrations for the fast suite.** The app runs on Postgres, but `ApiFactory` swaps in `SqliteAppDbContext`. Each test gets its own `DataSource=:memory:` connection, held open for the test's lifetime, with the SQLite migrations applied. No container is needed.
- **Testcontainers Postgres only for provider behaviour.** Migrations, collations and column types, locking and concurrency go in `tests/AppTemplate.Api.PostgresTests` (`PostgresTestBase`: one container per run, one database per test, on rootless Podman). Everything else stays in the fast suite.
- **No EF `InMemory` provider.** `UseInMemoryDatabase` has different behavior from real providers (no constraints, no transactions, no SQL translation). `NoAssembly_ReferencesEfCoreInMemory` fails the build if any project references it.

```csharp
// DO: the test base wires SQLite, FakeTimeProvider and the fixed signing key
public sealed class RenameProjectTests : ProjectTestBase { /* ... */ }

// DON'T
options.UseInMemoryDatabase("TestDb");
new PostgreSqlBuilder(...).Build(); // in the fast suite: provider tests belong in AppTemplate.Api.PostgresTests
```

## Assertions

- **Shouldly only.** No xUnit `Assert.*`, no FluentAssertions. For ProblemDetails responses use the extensions in `ApiResponseShouldly`: `ShouldSucceed()`, `ShouldFailWith("project.name_taken", HttpStatusCode.Conflict)` and `ShouldHaveFieldError("name", "project_name.too_long")`.
- **Pass `TestContext.Current.CancellationToken`** wherever the xUnit analyzer asks for a token. Warnings are errors.
- **Assert a collection's size before looping over it.** An empty loop passes vacuously.

## Test Structure

- **AAA pattern with clear separation.** Arrange, Act, Assert, separated by blank lines. Each section should be immediately identifiable.

```csharp
[Fact]
[Covers("rename_project", "project.name_taken")]
public async Task Preview_NameOfOtherActiveProject_ReturnsNameTaken()
{
    var alpha = await CreateProject("Alpha");
    var beta = await CreateProject("Beta");

    var p = await Preview("rename_project", new { projectId = beta, name = " ALPHA" });

    p.ShouldFailWith("project.name_taken", HttpStatusCode.Conflict);
    p.Body.GetProperty("details").GetProperty("existingProjectId").GetGuid().ShouldBe(alpha);
}
```

- **One assertion concept per test.** You may assert multiple properties of the same result, but do not test two separate behaviors in one test. Separate behaviors need separate tests so failures are specific.
- **Use the helpers.** `ApiTestBase` provides `Preview(name, input)`, `Execute(name, input, token)`, `PreviewAndExecute(name, input)`, `Query(name, input)` and `TokenOf(preview)`.

## Naming and Coverage

- **Test naming: `Method_State_Expected`.** Clear, searchable, and self-documenting. The test name is the specification.

```
PreviewAndExecute_NewName_RenamesProject
Execute_TaskAddedSincePreview_ReturnsStaleVersionChanged
```

- **Every action has a test class named `{ActionClass}Tests`** (e.g. `RenameProjectTests` for `RenameProject`).
- **Mark coverage with `[Covers]`.** Use `[Covers("action", "error.code")]`, or `[Covers("action", Case.HappyPath)]` / `[Covers("action", Case.Stale)]`. `CoverageTests` in `tests/AppTemplate.Architecture.Tests` fails unless every action has a happy path, each declared error code (including `validation.failed` when a validator exists) and, for mutations, a stale case.

## Fixtures and Mocking

- **One fresh database and host per test.** `ApiTestBase` builds a new `ApiFactory` per test, so tests never share state and need no ordering. Feature tests derive from a base that passes `includeFixtures: false` (see `ProjectTestBase`).
- **Framework tests use the Notes fixture, never the example feature.** Tests under `Actions/`, `Mcp/`, `OpenApi/`, `Telemetry/`, `Http/` and `Infrastructure/` use only the `fixture_*` Note actions in `Fixtures/Notes`. They are registered through `ActionRegistryOptions.Types` (`NoteFixture.Register`), never by adding the test assembly to `ActionRegistryOptions.Assemblies`: it holds deliberately invalid handlers.
- **No mocking frameworks for things you own.** If you control the code, use a real or test implementation. Mocking your own interfaces couples tests to implementation details and makes refactoring painful. Reserve mocks for third-party boundaries you cannot control. Control time with `FakeTimeProvider` (`Factory.Time`).

## Behavior Over Implementation

- **Test behavior, not implementation details.** Assert on the observable outcome (HTTP response, database state, published event), not on which internal methods were called. Tests coupled to internals break on every refactor.
