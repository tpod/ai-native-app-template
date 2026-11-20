---
name: ef-core
description: Use when mapping an aggregate or value object with EF Core, adding or changing a migration, or making a schema change that must keep existing data. Template-specific delta on the kit's ef-core skill.
---

# EF Core in this template

Base: `dotnet-claude-kit:ef-core`. Differences in this template:

- **SQLite in v1**, through `CommunityToolkit.Aspire.Hosting.SQLite` in the AppHost and `UseSqlite` in `src/AppTemplate.Api/Program.cs`. Postgres only via `skill:swap-to-postgres`.
- **Tests run on in-memory SQLite with the real migrations** (`tests/AppTemplate.Api.Tests/Infrastructure/SqliteTestDatabase.cs`), not Testcontainers. The EF `InMemory` provider is banned (`NoAssembly_ReferencesEfCoreInMemory`).
- **No repositories.** Handlers use `ctx.Db` (`AppDbContext`) directly. Shared reads go in a static `<Aggregate>Lookup` class next to the actions (`src/AppTemplate.Api/Features/Projects/ProjectLookup.cs`).
- **Handlers never call `SaveChanges`.** The action pipeline saves on execute only, after it checks the preview token.
- **Single-value VOs are mapped by convention**, not per property.

Where the kit agrees, follow it: `IEntityTypeConfiguration<T>`, no lazy loading, `AsNoTracking()` plus `.Select()` projections for reads, never auto-migrate in production. For slow queries use `dotnet-data:optimizing-ef-core-queries`. Look up EF Core APIs on Microsoft Learn before using them (AGENTS.md → Docs-lookup rule).

## Where things live

| What | File |
|---|---|
| DbContext and DbSets | `src/AppTemplate.Api/Data/AppDbContext.cs` |
| One configuration per aggregate | `src/AppTemplate.Api/Data/<Aggregate>Configuration.cs`, like `src/AppTemplate.Api/Data/ProjectConfiguration.cs` |
| VO converter convention | `src/AppTemplate.Api/Data/ValueObjectConventions.cs` |
| Design-time factory (used by dotnet-ef) | `src/AppTemplate.Api/Data/AppDbContextDesignTimeFactory.cs` |
| Migrations | `src/AppTemplate.Api/Data/Migrations/` |
| Test-only entities (fixtures) | `IModelContributor` (`src/AppTemplate.Api/Data/IModelContributor.cs`); not in migrations |

`AppDbContext.OnModelCreating` calls `ApplyConfigurationsFromAssembly`, so a new configuration class is picked up automatically. Add a `DbSet<T>` property only for aggregate roots: `public DbSet<Project> Projects => Set<Project>();`.

## Mapping value objects

**Single-value VOs** implementing `IValueObject<TSelf, TValue>` (`ProjectName`, `ProjectId`) need nothing: `ValueObjectConventions.Apply` registers a converter (`v => v.Value`, `FromTrusted`) for every one in the Domain assembly. Configure only the column: `builder.Property(p => p.Name).IsRequired().HasMaxLength(ProjectName.MaxLength);`. Reuse the VO's constants for lengths.

**Multi-value VOs** (no `IValueObject`) map as an EF complex type: `builder.ComplexProperty(o => o.ShippingAddress, a => { a.Property(x => x.City).HasMaxLength(100); });`. Check the complex-type API for the installed EF Core version on Microsoft Learn first. Complex types need a parameterless (private) constructor for materialization, like entities.

**Enums** are stored as strings: `builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);`. Renaming an enum member is then a data change: it needs a migration that updates the rows.

## Aggregate configuration checklist

Follow `src/AppTemplate.Api/Data/ProjectConfiguration.cs`:

```csharp
builder.ToTable("Projects");
builder.HasKey(p => p.Id);
builder.Property(p => p.Id).ValueGeneratedNever();          // ids come from ctx.NewId()
builder.Property(p => p.Version).IsConcurrencyToken();      // required for every aggregate root
builder.Ignore(p => p.DomainEvents);
builder.HasMany(p => p.Tasks).WithOne().HasForeignKey("ProjectId").IsRequired().OnDelete(DeleteBehavior.Cascade);
builder.Navigation(p => p.Tasks).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_tasks");
```

- **`Version` must be a concurrency token.** The pipeline increments it on every execute that changes something and relies on `DbUpdateConcurrencyException` to detect a concurrent write (`preview.stale` with reason `concurrent_write`). `AggregateRoots_VersionIsConcurrencyToken` checks every aggregate root in the model.
- `ValueGeneratedNever()` on every id. A database-generated id breaks preview replay.
- Child collections use the backing field, and the foreign key is a shadow property.
- Changing a child row must bump the root's `Version`. The pipeline does this as long as the child is loaded through the root (`Include`).

## Migrations

Run `dotnet tool restore` once per clone. dotnet-ef needs restored projects, so in a fresh clone run `dotnet build AppTemplate.slnx` first, or it fails with `NETSDK1004`. Then, from the repo root:

```bash
dotnet tool run dotnet-ef migrations add <Name> -p src/AppTemplate.Api -o Data/Migrations
```

- Name it for the change (`AddProjectDescription`), never `Update1`.
- Read the generated `Up` and `Down` before committing. Check that nothing is dropped that should be renamed.
- The Api migrates itself only in Development (`Program.cs`). Production applies migrations explicitly (a bundle or a script); never call `Migrate()` at startup there.
- `PersistenceTests.Model_AfterMigrations_HasNoPendingChanges` (`tests/AppTemplate.Api.Tests/Data/PersistenceTests.cs`) fails when the model and migrations disagree.
- To undo the last migration before it is shared: `dotnet tool run dotnet-ef migrations remove -p src/AppTemplate.Api`.

## Postgres-portable rule

Keep provider details inside `src/AppTemplate.Api/Data`. Feature and domain code never use `EF.Functions` provider-specific functions, raw SQL, `FromSql` or collation names. Today there is exactly one SQLite-specific setting: `UseCollation("NOCASE")` on `Project.Name`, marked with a comment and covered by `skill:swap-to-postgres`. Any new provider-specific setting gets the same comment and a line in that skill.

Case-insensitive comparisons belong in the column definition (collation), not in `ToLower()` calls in queries.

## Data-preserving changes

Migrations run against real data. Before committing, ask what happens to existing rows.

| Change | Safe way |
|---|---|
| Rename a property or column | EF may scaffold drop + add, which loses data. Edit the migration to `migrationBuilder.RenameColumn(...)` |
| Add a required column | Give it a default in the migration (`defaultValue:`), or add it nullable, backfill with `migrationBuilder.Sql(...)`, then make it required in a second migration |
| Tighten a VO rule (shorter `MaxLength`) | First find and fix rows that break the new rule (a `migrationBuilder.Sql` update), then change the column. `FromTrusted` loads old values without validating them, so bad rows would otherwise survive |
| Rename an enum member stored as a string | `migrationBuilder.Sql("UPDATE … SET Status = 'New' WHERE Status = 'Old'")` |
| Split or merge tables | Add the new shape, copy data with `migrationBuilder.Sql`, then drop the old shape in a later migration |

SQLite cannot alter most column properties in place. EF rebuilds the table for those operations. Read the generated migration to confirm the rebuild copies every column.

If the change also renames an action's input or output field, it is a contract change too: `skill:change-action-contract`.
