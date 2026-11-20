---
name: swap-to-postgres
description: Use when moving the app's database from SQLite to PostgreSQL, covering the AppHost resource, Npgsql provider registration, the case-insensitive name collation, migration regeneration and the test database decision.
---

# Swap SQLite for PostgreSQL

v1 runs on SQLite. This skill moves the app to PostgreSQL. It has not been dry-run in the template, because the template's CI has no Docker. Look up every package and API it names before using it (AGENTS.md → Docs-lookup rule): Microsoft Learn for Aspire and EF Core, the Npgsql EF Core provider docs for `citext` and collations.

**Docker (or another container runtime Aspire supports) is required** on every machine that runs the AppHost, and on every machine that runs the tests if you choose Testcontainers (step 6).

## 1. Packages (`Directory.Packages.props`, central versions)

| Package | Project | Version rule |
|---|---|---|
| `Aspire.Hosting.PostgreSQL` | `src/AppTemplate.AppHost/AppTemplate.AppHost.csproj` | Same version as `Aspire.Hosting.AppHost` (17.x) |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | `src/AppTemplate.Api/AppTemplate.Api.csproj` | Same major version as `Microsoft.EntityFrameworkCore.Design` (10.x) |

Add a `<PackageVersion>` line per package to `Directory.Packages.props` and a versionless `<PackageReference>` to the project. Remove `CommunityToolkit.Aspire.Hosting.SQLite` from the AppHost. Keep `Microsoft.EntityFrameworkCore.Sqlite` in the Api only if the tests stay on SQLite (step 6, option B).

## 2. AppHost resource (`src/AppTemplate.AppHost/AppHost.cs`)

Replace

```csharp
var db = builder.AddSqlite("appdb", builder.AppHostDirectory, "app.db");
```

with

```csharp
var db = builder.AddPostgres("postgres").WithDataVolume().AddDatabase("appdb");
```

Keep the database name `appdb`: the Api reads `ConnectionStrings:appdb`, and `.WithReference(db).WaitFor(db)` on the `api` resource stays as it is. `WithDataVolume()` keeps data across AppHost restarts. Update the comment above the line.

## 3. Provider registration (`src/AppTemplate.Api/`)

- `src/AppTemplate.Api/Program.cs`: replace `o.UseSqlite(builder.Configuration.GetConnectionString("appdb"))` with `o.UseNpgsql(builder.Configuration.GetConnectionString("appdb"))`. Keep `AddDbContext`. Do not switch to Aspire's `AddNpgsqlDbContext`: it pools contexts by default, and a pooled context needs a constructor that takes only `DbContextOptions`, while `AppDbContext` also takes `IEnumerable<IModelContributor>`.
- `src/AppTemplate.Api/Data/AppDbContextDesignTimeFactory.cs`: replace `UseSqlite("Data Source=design.db")` with `UseNpgsql("Host=localhost;Database=design;Username=postgres;Password=postgres")`. dotnet-ef needs a provider to scaffold migrations; it does not connect for `migrations add`.
- The Development-only `Database.Migrate()` in `Program.cs` now runs against the Postgres container. Nothing else changes.

## 4. Replace the `NOCASE` collation

`src/AppTemplate.Api/Data/ProjectConfiguration.cs` maps `Project.Name` with `.UseCollation("NOCASE")`, a SQLite collation. It makes `ProjectLookup.FindNameHolder` (`src/AppTemplate.Api/Features/Projects/ProjectLookup.cs`) find "Alpha" for " alpha " after `ProjectName` trims the input. That is the `project.name_taken` rule. Postgres compares text case-sensitively, so the rule silently breaks if you only delete the collation.

**Recommended: a `citext` column.** It is the smallest change: the query, the domain and the tests stay as they are.

```csharp
// ProjectConfiguration.cs
// Postgres-specific: citext compares case-insensitively. See swap-to-postgres.
builder.Property(p => p.Name).IsRequired().HasMaxLength(ProjectName.MaxLength).HasColumnType("citext");

// AppDbContext.OnModelCreating, before the configurations are applied
modelBuilder.HasPostgresExtension("citext");
```

Check in the Npgsql docs how `HasMaxLength` combines with `citext`: `citext` has no length modifier, so enforce the length with the VO only. Ordering by `Name` (`list_projects`) also becomes case-insensitive, as it was with `NOCASE`.

**Alternative: a normalized shadow column.** Add `NormalizedName` (lowercase, trimmed) next to `Name`, keep it in sync on every write (set it in the configuration through a value generator or in `SaveChanges`), compare against it in `ProjectLookup.FindNameHolder`, and index it. It is portable across providers and needs no extension, but it adds code that can drift. Use it only if `citext` is not available on your Postgres host.

The Npgsql provider also supports nondeterministic ICU collations (`HasCollation(...)` + `UseCollation(...)`). Read its docs on their limits (pattern matching with `LIKE`) before choosing them over `citext`.

Then update the comment on `ProjectLookup.FindNameHolder`, which mentions `NOCASE`.

## 5. Regenerate the migrations

SQLite migrations do not run on Postgres. Start the history again:

```bash
git rm -r -q src/AppTemplate.Api/Data/Migrations
dotnet tool restore
dotnet build AppTemplate.slnx
dotnet tool run dotnet-ef migrations add InitialCreate -p src/AppTemplate.Api -o Data/Migrations
```

Read the new `InitialCreate`: `uuid` ids, `citext` for `Name`, an `AlterDatabase` that enables the `citext` extension, `integer` for `Version`. Existing SQLite data is not migrated; export and import it separately if a deployed SQLite database holds real data.

## 6. Tests: keep SQLite or move to Testcontainers

Today `tests/AppTemplate.Api.Tests/Infrastructure/ApiFactory.cs` and `tests/AppTemplate.Api.Tests/Infrastructure/SqliteTestDatabase.cs` run every test on in-memory SQLite with the real migrations. AGENTS.md (dotnet-claude-kit overrides table) and `.claude/rules/testing.md` state that as the rule. After step 5 the migrations are Postgres migrations, so that setup no longer applies them as-is, and `PersistenceTests.Model_AfterMigrations_HasNoPendingChanges` compares a SQLite model with a Postgres snapshot.

| | A. Testcontainers Postgres (recommended) | B. Keep SQLite for tests |
|---|---|---|
| What runs | A real Postgres container (`Testcontainers.PostgreSql`), the real migrations | SQLite with a separate schema: a second migrations set, or `EnsureCreated()` plus provider-specific branches in the configurations |
| Fidelity | Tests prove `citext`, constraints and SQL translation as production runs them | The case-insensitive-name tests prove SQLite's behavior, not Postgres's |
| Speed and setup | One container per test run (start it in an assembly fixture), one fresh database per test; container start adds seconds | No container, as fast as today |
| Requirements | Docker on every dev machine and in CI; `scripts/verify.sh` needs it too | None |
| Drift risk | Low | High: two schemas to keep in step |

Choose A unless Docker is unavailable where tests run. For A:

1. Add `Testcontainers.PostgreSql` (central version) to `tests/AppTemplate.Api.Tests/AppTemplate.Api.Tests.csproj`.
2. Replace `SqliteTestDatabase` (`tests/AppTemplate.Api.Tests/Infrastructure/SqliteTestDatabase.cs`) with a Postgres equivalent that keeps its members, `NewContext(...)` and `Migrate(...)`, so `PersistenceTests`, `ActionRegistryTests` and `ChangeSetTests` keep compiling. It needs one container for the run, a uniquely named database per instance, `Database.Migrate()`, then each `IModelContributor`'s `SchemaSql`. Rewrite `NoteModelContributor.SchemaSql` (`tests/AppTemplate.Api.Tests/Fixtures/Notes/NoteModelContributor.cs`) in Postgres DDL (`uuid`, `text`, `boolean`, `integer`).
3. In `ApiFactory.ConfigureWebHost`, register `UseNpgsql(<per-test connection string>)` instead of `UseSqlite(_database.Connection)`.
4. Switch the architecture guard off SQLite. `AggregateRoots_VersionIsConcurrencyToken` in `tests/AppTemplate.Architecture.Tests/ArchitectureTests.cs` builds the model with `SqliteConnection` and `UseSqlite`. That project gets SQLite only through the Api, so it stops compiling once step 1 removes `Microsoft.EntityFrameworkCore.Sqlite`. Make it build the model with Npgsql instead; that checks the model production actually uses. Replace

   ```csharp
           using var connection = new SqliteConnection("DataSource=:memory:");
           connection.Open();
           var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
   ```

   with

   ```csharp
           // Builds the model only; Npgsql opens no connection for that.
           var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options;
   ```

   Then delete `using Microsoft.Data.Sqlite;` at the top of the file. `UseNpgsql` resolves through the Api's Npgsql reference and `using Microsoft.EntityFrameworkCore;`; confirm the namespace in the installed package before building. Then check: `dotnet test --project tests/AppTemplate.Architecture.Tests` must pass with 0 skipped.
5. Keep the EF `InMemory` ban; it still applies.
6. Update the docs that state SQLite: the "Integration tests on Testcontainers Postgres" row of AGENTS.md's overrides table (the template now agrees with the kit), the architecture-map and Commands lines that mention SQLite `appdb`, `.claude/rules/testing.md`, `skill:ef-core` and `skill:tdd`.

## 7. Verify

```bash
scripts/verify.sh
```

It must print `VERIFY OK`. Then run `dotnet run --project src/AppTemplate.AppHost` with Docker running, and check in the Aspire dashboard that `postgres` and `appdb` are healthy before `api` starts. Create two projects named "Alpha" and " alpha " through the `app` MCP server or the UI: the second must fail with `project.name_taken`.

Finally, update AGENTS.md's "Swap to Postgres" section and the `AddSqlite` row of its overrides table to describe the new state.
