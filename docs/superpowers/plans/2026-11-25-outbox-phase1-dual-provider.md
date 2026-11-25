# Outbox Phase 1: Postgres Runtime, SQLite Fast Tests — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The app runs on Postgres everywhere, local AppHost included, on Podman. SQLite remains only as the fast test database. A separate Testcontainers project proves the Postgres path.

**Architecture:** `AppDbContext` becomes an abstract base with two provider subclasses, `PostgresAppDbContext` (runtime) and `SqliteAppDbContext` (fast tests). Each subclass has its own migration set, following EF Core's "one context type per provider" pattern. Provider-specific mapping is expressed once as a provider-neutral annotation and translated by each subclass inside `Data/`. `scripts/verify.sh` runs the Postgres project against rootless Podman and never skips it silently.

**Tech Stack:** .NET 10, EF Core 10.0.15, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.4 (match EF's major version; 11.x targets EF 11), `Aspire.Hosting.PostgreSQL` 17.0.0, `Testcontainers.PostgreSql` 4.17.0, xUnit v3 on MTP, Shouldly, Podman 5.7.0 (installed; socket `/run/user/1000/podman/podman.sock`).

**Spec:** `docs/superpowers/specs/2026-11-24-outbox-design.md`, §1 "Database providers", §7 "Testing" (Postgres, container runtime, `verify.sh`), and "Delivery order" item 1. Phases 2 (outbox) and 3 (webhooks) get their own plans after this one lands.

## Global Constraints

- Postgres is the only runtime database: `Program.cs` registers `UseNpgsql` only. There is no provider config switch.
- SQLite exists only so the fast test suite runs without a container: `ApiFactory` substitutes `SqliteAppDbContext`.
- Provider details stay in `src/AppTemplate.Api/Data` (golden rule 6). No collation names or column types in feature or domain code.
- The connection string name stays `appdb`.
- Add packages with `dotnet add package` (central versions in `Directory.Packages.props`, no `Version=` in csproj). Use the exact versions in Tech Stack.
- Look up every EF Core, Npgsql, Aspire and Testcontainers API before using it (AGENTS.md Docs-lookup rule), then confirm it against `~/.nuget/packages/<id>/<version>/`.
- Never run `aspire agent init`, `/dotnet-init`, `pkill`, `rm -rf`, `git reset --hard`.
- MTP exit code 8 means zero tests matched: a failure, not a pass. Check `$?`.
- Each task ends with `scripts/verify.sh` printing `VERIFY OK` (from Task 3 on, with the Postgres tests included) and a Conventional Commit with the session's attribution trailer.

## Review Focus

1. **Podman socket not running** (after a reboot, or with no user session): `verify.sh` must fail fast and print `systemctl --user start podman.socket`. It must not hang in Testcontainers or report a green build. Pinned in Task 3, Step 4.
2. **A schema change generated for only one provider:** the build must go red. Pinned by `Migrations_AreUpToDate` for both contexts in Task 1.
3. **Leaked containers** after a Postgres test run, including with Ryuk disabled: `podman ps -a` must be empty afterwards. Pinned in Task 2, Step 6.
4. **Case-insensitive names on Postgres:** `project.name_taken` for " alpha " vs "Alpha", and `list_projects` ordering "alpha", "Beta", "charlie" regardless of case, as `NOCASE` did on SQLite. Pinned in Task 2, Step 1.
5. **Stale developer state after the switch:** an old `src/AppTemplate.AppHost/app.db` must not be picked up. A first AppHost run on an empty Postgres volume must migrate and serve `/health`. Pinned in Task 4, Step 4.

---

### Task 1: Provider-split DbContext and two migration sets

**Files:**
- Modify: `src/AppTemplate.Api/Data/AppDbContext.cs` (sealed → abstract base)
- Create: `src/AppTemplate.Api/Data/PostgresAppDbContext.cs`, `src/AppTemplate.Api/Data/SqliteAppDbContext.cs`
- Create: `src/AppTemplate.Api/Data/CaseInsensitiveText.cs`
- Modify: `src/AppTemplate.Api/Data/ProjectConfiguration.cs:16-17`
- Modify: `src/AppTemplate.Api/Data/AppDbContextDesignTimeFactory.cs` (becomes two factories)
- Delete: `src/AppTemplate.Api/Data/Migrations/*` (all five files)
- Create (generated): `src/AppTemplate.Api/Data/Migrations/Postgres/*`, `src/AppTemplate.Api/Data/Migrations/Sqlite/*`
- Create: `scripts/add-migration.sh`
- Modify: `src/AppTemplate.Api/Program.cs:11`
- Modify: `src/AppTemplate.Api/AppTemplate.Api.csproj` (add Npgsql)
- Modify: `src/AppTemplate.Api/Features/Projects/ProjectLookup.cs:18` (the comment that mentions NOCASE)
- Modify: `tests/AppTemplate.Api.Tests/Infrastructure/SqliteTestDatabase.cs:15-18`, `tests/AppTemplate.Api.Tests/Infrastructure/ApiFactory.cs:51-54`
- Modify: `tests/AppTemplate.Architecture.Tests/ArchitectureTests.cs:124-129`
- Test: `tests/AppTemplate.Architecture.Tests/ProviderModelTests.cs` (new)

**Interfaces:**
- Produces:
  - `public abstract class AppDbContext : DbContext` with `protected AppDbContext(DbContextOptions options, IEnumerable<IModelContributor> contributors)`. Public surface unchanged: `Projects`, `History`. Everything keeps injecting `AppDbContext`.
  - `public sealed class PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options, IEnumerable<IModelContributor> contributors) : AppDbContext`
  - `public sealed class SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options, IEnumerable<IModelContributor> contributors) : AppDbContext`
  - `internal static class CaseInsensitiveText` with `PropertyBuilder<string> IsCaseInsensitive(this PropertyBuilder<string> builder)`, which sets the annotation `"AppTemplate:CaseInsensitive" = true`. It also has `void ApplyForPostgres(ModelBuilder mb)` (each annotated property → `HasColumnType("citext")`, plus `mb.HasPostgresExtension("citext")` when any exist) and `void ApplyForSqlite(ModelBuilder mb)` (each annotated property → `UseCollation("NOCASE")`).
  - DI in `Program.cs`: `AddDbContext<AppDbContext, PostgresAppDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("appdb")))`.
  - `scripts/add-migration.sh <Name>` runs `dotnet tool run dotnet-ef migrations add <Name> -p src/AppTemplate.Api --context PostgresAppDbContext -o Data/Migrations/Postgres`, then the same with `--context SqliteAppDbContext -o Data/Migrations/Sqlite`. It exits non-zero if either fails.

- [ ] **Step 1: Write the failing tests** in `tests/AppTemplate.Architecture.Tests/ProviderModelTests.cs`. Build each context with no open connection: Npgsql `UseNpgsql("Host=localhost;Database=model_only")`, SQLite `UseSqlite("DataSource=:memory:")`.

```csharp
[Theory]
[InlineData(typeof(PostgresAppDbContext))]
[InlineData(typeof(SqliteAppDbContext))]
public void Migrations_AreUpToDate(Type contextType)
{
    using var context = NewContext(contextType);
    context.Database.HasPendingModelChanges().ShouldBeFalse($"Run scripts/add-migration.sh: {contextType.Name} has model changes without a migration.");
}

[Fact]
public void ProviderModels_HaveSameTablesAndColumns()
{
    using var pg = NewContext(typeof(PostgresAppDbContext));
    using var lite = NewContext(typeof(SqliteAppDbContext));
    var pgShape = Shape(pg);   // set of "Table.Column" from GetEntityTypes() / GetTableName() / GetColumnName()
    var liteShape = Shape(lite);
    pgShape.Count.ShouldBeGreaterThan(0);
    pgShape.ShouldBe(liteShape, ignoreOrder: true);
}

[Fact]
public void ProjectName_IsCaseInsensitiveOnBothProviders()
{
    using var pg = NewContext(typeof(PostgresAppDbContext));
    using var lite = NewContext(typeof(SqliteAppDbContext));
    NameProperty(pg).GetColumnType().ShouldBe("citext");
    NameProperty(lite).GetCollation().ShouldBe("NOCASE");
}
```

Mark `ProjectName_IsCaseInsensitiveOnBothProviders` with the same `GuardConditions` skip as `AggregateRoots_VersionIsConcurrencyToken`, so `skill:remove-example` stays green.

- [ ] **Step 2: Run the tests and confirm they fail.** Run `dotnet test --project tests/AppTemplate.Architecture.Tests --filter "FullyQualifiedName~ProviderModelTests"`. Expected: a compile failure, because `PostgresAppDbContext` is not defined.

- [ ] **Step 3: Add Npgsql.** Run `dotnet add src/AppTemplate.Api package Npgsql.EntityFrameworkCore.PostgreSQL --version 10.0.4`. Keep `Microsoft.EntityFrameworkCore.Sqlite`: `SqliteAppDbContext` and its migrations live in the Api.

- [ ] **Step 4: Implement the contexts, `CaseInsensitiveText` and the configuration change.**
  - Each subclass overrides `OnModelCreating`: call `base.OnModelCreating(mb)` first, then `CaseInsensitiveText.ApplyFor…(mb)`.
  - `ProjectConfiguration` line 17 becomes `.HasMaxLength(ProjectName.MaxLength).IsCaseInsensitive()`, with a comment saying the provider contexts translate it.
  - Keep `ContributorModelCacheKeyFactory` keyed on `context.GetType()`, so each provider caches its own model.
  - Replace `AppDbContextDesignTimeFactory` with `PostgresDesignTimeFactory` and `SqliteDesignTimeFactory` (`IDesignTimeDbContextFactory<…>` each), using the connection strings already shown in Step 1 (`Data Source=design.db` for SQLite).
  - Update the `ProjectLookup.FindNameHolder` comment: case-insensitivity now comes from `IsCaseInsensitive()`.

- [ ] **Step 5: Regenerate the migrations.** `git rm -q src/AppTemplate.Api/Data/Migrations/*.cs`, then `dotnet build AppTemplate.slnx && scripts/add-migration.sh InitialCreate`. Read both `InitialCreate` files:
  - The Postgres one has `uuid` ids, `citext` for `Name`, an `AlterDatabase` enabling `citext`, and a `History` identity column.
  - The SQLite one has `NOCASE` on `Name`.
  - No deployed database holds data, so restarting the migration history is safe (spec §1).

- [ ] **Step 6: Point the test infrastructure at `SqliteAppDbContext`.**
  - `SqliteTestDatabase.NewContext` builds `DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(Connection)` and returns `new SqliteAppDbContext(...)`.
  - `ApiFactory.ConfigureServices` removes the Postgres registration and registers `AppDbContext` as `SqliteAppDbContext` on `_database.Connection`. Read `~/.nuget/packages/microsoft.entityframeworkcore/10.0.15/` first. EF 10 also registers `IDbContextOptionsConfiguration<PostgresAppDbContext>`, so removing only `DbContextOptions<…>` can leave Npgsql configured.
  - `ArchitectureTests.AggregateRoots_VersionIsConcurrencyToken` builds a `SqliteAppDbContext`.

- [ ] **Step 7: Run everything.** Run `dotnet test --project tests/AppTemplate.Architecture.Tests` (expected: all pass, `ProviderModelTests` 4 passed) and `dotnet test --project tests/AppTemplate.Api.Tests` (expected: all pass, same count as before this task). Then add a throwaway nullable property to `HistoryEntry`. Run only `--context SqliteAppDbContext` through dotnet-ef and confirm `Migrations_AreUpToDate(PostgresAppDbContext)` fails. Then revert the property and that migration.

- [ ] **Step 8: Publish the migration command.** Make `scripts/add-migration.sh` executable. Update the AGENTS.md Commands row "Add a migration" to `scripts/add-migration.sh <Name>` (one command, both providers).

- [ ] **Step 9: Run `scripts/verify.sh` (expected: `VERIFY OK`), then commit.** Commit message: `refactor(data): split AppDbContext into Postgres and SQLite providers`.

---

### Task 2: Postgres test project on Testcontainers

**Files:**
- Create: `tests/AppTemplate.Api.PostgresTests/AppTemplate.Api.PostgresTests.csproj` (copy `AppTemplate.Api.Tests.csproj`'s MTP and xUnit settings; add `Testcontainers.PostgreSql`)
- Create: `tests/AppTemplate.Api.PostgresTests/PostgresContainerFixture.cs`, `PostgresApiFactory.cs`, `PostgresTestBase.cs`
- Create: `tests/AppTemplate.Api.PostgresTests/MigrationTests.cs`, `ProjectNamesTests.cs`, `ConcurrencyTokenTests.cs`
- Modify: `AppTemplate.slnx` (add the project under `/tests/`)
- Modify: `Directory.Packages.props` (via `dotnet add package`)

**Interfaces:**
- Consumes: `PostgresAppDbContext`, `AppDbContext` (Task 1). The app's own `UseNpgsql(ConnectionStrings:appdb)` registration is used as is, with no service replacement.
- Produces:
  - `public sealed class PostgresContainerFixture : IAsyncLifetime`, registered with xUnit v3 `[assembly: AssemblyFixture(typeof(PostgresContainerFixture))]`. It starts one `postgres:17-alpine` container per test run and exposes `Task<string> CreateDatabaseAsync()`, which issues `CREATE DATABASE "t_<guid N>"` and returns that database's connection string.
  - `public sealed class PostgresApiFactory(string connectionString) : WebApplicationFactory<Program>`. It uses environment `Testing`, settings `ConnectionStrings:appdb` = connectionString and `Preview:SigningKey` = `ApiFactory.SigningKey`'s value, and runs `Database.Migrate()` in `CreateHost`.
  - `public abstract class PostgresTestBase(PostgresContainerFixture pg) : IAsyncLifetime` with `HttpClient Client` and `Task<JsonElement> PreviewAndExecute(string action, object input)` / `Task<HttpResponseMessage> Preview(string action, object input)` / `Task<JsonElement> Query(string name, object input)`, mirroring `ApiTestBase` in behaviour (HTTP routes from AGENTS.md "Preview protocol").

- [ ] **Step 1: Write the failing tests.**

```csharp
// MigrationTests
[Fact]
public async Task Migrate_EmptyDatabase_AppliesAllMigrations()
{
    await using var db = NewContext();   // PostgresAppDbContext on a fresh database
    (await db.Database.GetPendingMigrationsAsync(Ct)).ShouldBeEmpty();
    (await db.Database.GetAppliedMigrationsAsync(Ct)).Count().ShouldBe(db.Database.GetMigrations().Count());
}

// ProjectNamesTests
[Fact]
public async Task CreateProject_NameDiffersOnlyByCase_ReturnsNameTaken()
{
    await PreviewAndExecute("create_project", new { name = "Alpha" });
    var p = await Preview("create_project", new { name = " alpha " });
    p.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    (await Code(p)).ShouldBe("project.name_taken");
}

[Fact]
public async Task ListProjects_MixedCase_OrdersCaseInsensitively()
{
    foreach (var n in new[] { "charlie", "Beta", "alpha" }) await PreviewAndExecute("create_project", new { name = n });
    var items = (await Query("list_projects", new { })).EnumerateArray().Select(i => i.GetProperty("name").GetString()).ToList();
    items.ShouldBe(["alpha", "Beta", "charlie"]);
}

// ConcurrencyTokenTests
[Fact]
public async Task SaveChanges_ProjectChangedByOtherContext_ThrowsConcurrencyException()
{
    // create a project via the API, load it in two contexts, rename + IncrementVersion + save in the first,
    // then rename + IncrementVersion + save in the second
    await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));
}
```

Check the `list_projects` response shape and `create_project` input against `tests/AppTemplate.Api.Tests/Features/Projects/` before finishing the assertions.

- [ ] **Step 2: Run the tests and confirm they fail.** Run `dotnet test --project tests/AppTemplate.Api.PostgresTests`. Expected: a compile failure, because the fixture types are missing.

- [ ] **Step 3: Implement the fixture, factory and base.** Run `dotnet add tests/AppTemplate.Api.PostgresTests package Testcontainers.PostgreSql --version 4.17.0`.
  - Read Testcontainers for .NET's docs on Podman and on Ryuk before writing the fixture.
  - The fixture relies on `DOCKER_HOST` being set (Task 3 sets it in `verify.sh`). When the runtime is unreachable, it throws with the message `No container runtime: start Podman with 'systemctl --user start podman.socket' and set DOCKER_HOST=unix://$XDG_RUNTIME_DIR/podman/podman.sock`.

- [ ] **Step 4: Run the tests and confirm they pass.** Run `DOCKER_HOST=unix://$XDG_RUNTIME_DIR/podman/podman.sock dotnet test --project tests/AppTemplate.Api.PostgresTests`. Expected: 4 passed, exit 0. If Ryuk fails under rootless Podman, try `TESTCONTAINERS_RYUK_CONTAINER_PRIVILEGED=true` first, then `TESTCONTAINERS_RYUK_DISABLED=true`, and record which one worked. Task 3 bakes it into `verify.sh`.

- [ ] **Step 5: Prove the test run can fail.** Temporarily change the expected order in `ListProjects_MixedCase_OrdersCaseInsensitively`. Run it and confirm a non-zero exit, then revert.

- [ ] **Step 6: Check for leaked containers.** After a full run, `podman ps -a --format '{{.Image}}' | grep -c postgres || true` prints `0`. The Ryuk container itself may linger for about 10 s; recheck after it exits.

- [ ] **Step 7: Commit.** Commit message: `test(data): run focused Postgres tests on Testcontainers`.

---

### Task 3: `verify.sh` runs the Postgres tests against Podman

**Files:**
- Modify: `scripts/verify.sh:13-14` (the `dotnet test --solution` line)

**Interfaces:**
- Consumes: `tests/AppTemplate.Api.PostgresTests` (Task 2), plus the Ryuk setting recorded in Task 2, Step 4.
- Produces: the environment contract that AGENTS.md documents in Task 5.
  - `VERIFY_POSTGRES` defaults to `1`.
  - `DOCKER_HOST` defaults to `unix://$XDG_RUNTIME_DIR/podman/podman.sock` when unset and that socket exists.
  - Output lines are `==> dotnet test (sqlite)`, `==> dotnet test (postgres)`, and `POSTGRES TESTS SKIPPED` (only when `VERIFY_POSTGRES=0`).

- [ ] **Step 1: Replace the single test line.**
  - Run `dotnet test --project tests/AppTemplate.Architecture.Tests --no-build`, then `dotnet test --project tests/AppTemplate.Api.Tests --no-build`.
  - Then, unless `VERIFY_POSTGRES=0`, run a runtime check: `curl -sf --unix-socket "${DOCKER_HOST#unix://}" http://d/_ping` must print `OK`. On failure, print the Task 2 message and `exit 1`.
  - Then run `dotnet test --project tests/AppTemplate.Api.PostgresTests --no-build`.
  - With `VERIFY_POSTGRES=0`, print `POSTGRES TESTS SKIPPED` instead.
  - Keep `set -euo pipefail` so each `dotnet test` exit code (including 8) fails the script.

- [ ] **Step 2: Run the green path.** Run `scripts/verify.sh`. Expected: the output contains `==> dotnet test (postgres)` and ends with `VERIFY OK`.

- [ ] **Step 3: Run the skip path.** Run `VERIFY_POSTGRES=0 scripts/verify.sh`. Expected: `POSTGRES TESTS SKIPPED` appears before `VERIFY OK`.

- [ ] **Step 4: Run the failure path.** Run `DOCKER_HOST=unix:///nonexistent.sock scripts/verify.sh; echo "exit=$?"`. Expected: the Podman message and `exit=1`, reached within seconds, with no hang.

- [ ] **Step 5: Commit.** Commit message: `build(verify): run Postgres tests on Podman and never skip silently`.

---

### Task 4: AppHost runs Postgres on Podman

**Files:**
- Modify: `src/AppTemplate.AppHost/AppHost.cs:5-6`
- Modify: `src/AppTemplate.AppHost/AppTemplate.AppHost.csproj` (swap packages)
- Modify: `src/AppTemplate.AppHost/Properties/launchSettings.json` (both profiles)
- Modify: `Directory.Packages.props` (add `Aspire.Hosting.PostgreSQL`, remove `CommunityToolkit.Aspire.Hosting.SQLite`)
- Modify: `.gitignore` (only if it has a line or comment specific to `app.db`; `*.db` can stay, since tests and design-time factories may still create SQLite files)

**Interfaces:**
- Consumes: the `appdb` connection string name that `Program.cs` reads (Task 1).
- Produces: the AppHost resource `postgres` with the database `appdb`, plus `ASPIRE_CONTAINER_RUNTIME=podman` in every launch profile.

- [ ] **Step 1: Swap the packages.** Run `dotnet add src/AppTemplate.AppHost package Aspire.Hosting.PostgreSQL --version 17.0.0` and `dotnet remove src/AppTemplate.AppHost package CommunityToolkit.Aspire.Hosting.SQLite`. Delete its `PackageVersion` line.

- [ ] **Step 2: Replace the resource.** Replace it with `builder.AddPostgres("postgres").WithDataVolume().AddDatabase("appdb")`. Look up the Aspire 17 signatures on Microsoft Learn first. Keep `.WithReference(db).WaitFor(db)` on the api. Replace the "SQLite file lives next to the AppHost" comment with one saying the data lives in a Podman volume.

- [ ] **Step 3: Set the runtime.** Add `"ASPIRE_CONTAINER_RUNTIME": "podman"` to the `http` and `https` profiles. Confirm the variable name in the Aspire docs.

- [ ] **Step 4: Run the AppHost against an empty volume.**
  - Start `dotnet run --project src/AppTemplate.AppHost` as a background task.
  - Wait until `curl -sf http://localhost:5180/health` succeeds, at most 180 s.
  - Then run `create_project` preview and execute over HTTP (`POST /api/actions/create_project/preview`, then execute with the token) and `list_projects`. Expected: the project is listed.
  - Stop the background task through its task id, not `pkill`.
  - Then `podman volume ls` shows the AppHost's Postgres volume. A leftover `src/AppTemplate.AppHost/app.db` is ignored.

- [ ] **Step 5: Run `scripts/verify.sh` (expected: `VERIFY OK`), then commit.** Commit message: `feat(apphost): run local Postgres on Podman instead of SQLite`.

---

### Task 5: Documentation and skills

**Files:**
- Modify: `AGENTS.md` (the architecture map AppHost line; the Commands table rows for migrations, running and `VERIFY_POSTGRES`; the skill map row `Move to Postgres`; the "Swap to Postgres" section; the overrides rows "Integration tests on Testcontainers Postgres" and "Aspire examples with AddPostgres"; "Install once per machine", which gains Podman)
- Delete: `.claude/skills/swap-to-postgres/` (the template now runs Postgres; the skill would mislead)
- Modify: `.claude/skills/ef-core/SKILL.md:74` (provider-specific settings → `IsCaseInsensitive()` and the two contexts; migrations → `scripts/add-migration.sh`)
- Modify: `.claude/skills/tdd/SKILL.md` and `.claude/rules/testing.md` (SQLite fast suite plus the Postgres project: when a test belongs there)
- Modify: `.claude/skills/remove-example/SKILL.md:187` (the section it edits is renamed)
- Modify: `.claude/skills/add-aggregate/SKILL.md` (if it names the old migration command)

**Interfaces:**
- Consumes: the commands and names from Tasks 1–4.

- [ ] **Step 1: Find every stale reference.** Run `grep -rn -i "swap-to-postgres\|AddSqlite\|NOCASE\|migrations add\|v1 targets SQLite\|app\.db" AGENTS.md .claude docs/superpowers/2026-11-20-template-v1-followups.md scripts || true`. Every hit outside `docs/superpowers/specs` and `plans` is either fixed in this task or deliberately kept with a reason in the commit body.

- [ ] **Step 2: Rewrite the AGENTS.md "Swap to Postgres" section as "Databases".**
  - Postgres is the runtime database on Podman.
  - SQLite is for the fast suite only.
  - `tests/AppTemplate.Api.PostgresTests` is for provider behaviour.
  - `scripts/add-migration.sh` generates both migration sets.
  - Case-insensitive text uses `IsCaseInsensitive()`.
  - One-time Podman setup: `sudo apt-get install podman`, `systemctl --user enable --now podman.socket`.

- [ ] **Step 3: Edit the skills and rules** listed under Files. Then rerun the Step 1 grep. Expected: only the deliberate hits remain.

- [ ] **Step 4: Run `scripts/verify.sh` (expected: `VERIFY OK`), then commit.** Commit message: `docs: describe Postgres runtime, SQLite fast tests and Podman setup`.
