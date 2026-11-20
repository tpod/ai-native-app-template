---
name: add-aggregate
description: Use when adding a new aggregate root (a new kind of thing with its own id, table and actions), including its id value object, EF configuration, migration and first action.
---

# Add an aggregate root

Replace `<Aggregate>` with the singular PascalCase name (`Invoice`), `<Aggregates>` with the plural (`Invoices`) and `<area>` with the snake_case error area (`invoice`). Mirror the Projects example file for file. Details per step are in `skill:domain-modeling`, `skill:ef-core`, `skill:add-action` and `skill:tdd`. Write the tests of step 5 before the code they test.

## 1. Domain: `src/AppTemplate.Domain/<Aggregates>/`

| File | Content | Example |
|---|---|---|
| `<Aggregate>Id.cs` | `public readonly record struct <Aggregate>Id : IValueObject<<Aggregate>Id, Guid>` with a private constructor, `Value`, `From(Guid)` and `FromTrusted(Guid)` | `src/AppTemplate.Domain/Projects/ProjectId.cs` |
| One file per VO | `sealed record`, private constructor, `Create` returning `OneOf<TVo, ValidationError>`, `FromTrusted` | `src/AppTemplate.Domain/Projects/ProjectName.cs` |
| `<Aggregate>.cs` | `public sealed class <Aggregate> : AggregateRoot<<Aggregate>Id>`, a private EF constructor, a static `Create(...)` returning OneOf, behavior methods returning OneOf | `src/AppTemplate.Domain/Projects/Project.cs` |
| `<Aggregate>Errors.cs` | At least `[Error("<area>.not_found", ErrorKind.NotFound, "<Aggregate> not found")]` | `src/AppTemplate.Domain/Projects/ProjectErrors.cs` |
| `<Aggregate>Events.cs` | `<Aggregate>Created` and other past-tense events implementing `IDomainEvent` | `src/AppTemplate.Domain/Projects/ProjectEvents.cs` |

The Domain project references only OneOf. Do not add packages to it.

## 2. Persistence: `src/AppTemplate.Api/Data/`

1. Add `<Aggregate>Configuration.cs`: an `internal sealed class` implementing `IEntityTypeConfiguration<<Aggregate>>`. Copy the checklist from `src/AppTemplate.Api/Data/ProjectConfiguration.cs`: `ToTable`, `HasKey`, `ValueGeneratedNever()` on the id, `Version` as concurrency token, `Ignore(x => x.DomainEvents)`, column lengths from the VO constants, child collections through their backing field.
2. Add the DbSet to `src/AppTemplate.Api/Data/AppDbContext.cs`: `public DbSet<<Aggregate>> <Aggregates> => Set<<Aggregate>>();`.
3. VOs implementing `IValueObject` are mapped by `ValueObjectConventions` automatically. Do not add converters for them.
4. Add the migration:

```bash
dotnet tool restore
dotnet tool run dotnet-ef migrations add Add<Aggregates> -p src/AppTemplate.Api -o Data/Migrations
```

Read the generated `Up`: one `CreateTable` for the root, one per child entity, and no changes to other tables.

## 3. Api: `src/AppTemplate.Api/Features/<Aggregates>/`

| File | Content | Example |
|---|---|---|
| `<Aggregate>Lookup.cs` | `internal static class` with `Find(Guid id, ActionContext ctx)` returning the tracked aggregate (with `Include` for children) or null | `src/AppTemplate.Api/Features/Projects/ProjectLookup.cs` |
| `<Aggregate>Dtos.cs` | `<Aggregate>Dto` with `static From(<Aggregate>)`, returned by mutations and the get query; a summary DTO for lists | `src/AppTemplate.Api/Features/Projects/ProjectDtos.cs` |
| `<Aggregate>Validators.cs` | One `AbstractValidator<TInput>` per input | `src/AppTemplate.Api/Features/Projects/ProjectValidators.cs` |
| `Create<Aggregate>.cs` | The first action. `Load` validates, calls `<Aggregate>.Create(<Aggregate>Id.From(ctx.NewId()), …)` and adds it to `ctx.Db.<Aggregates>`; `Apply` returns `new Success()` | `src/AppTemplate.Api/Features/Projects/CreateProject.cs` |
| `Get<Aggregate>.cs` | A `[Query]` returning the DTO or `<area>.not_found`, so agents can read what they created | `src/AppTemplate.Api/Features/Projects/GetProject.cs` |

Follow `skill:add-action` for each action. Nothing needs registering: the registry and the validator scan find them.

## 4. Re-enable the guards `skill:remove-example` skipped

If the template's example was removed, six architecture tests are skipped until an aggregate exists. Find them:

```bash
git grep -n "No aggregate yet" -- tests/AppTemplate.Architecture.Tests
```

Change each `[Fact(Skip = "No aggregate yet: skill:add-aggregate removes this Skip")]` back to `[Fact]`. They must pass now: they check that entities have no public setters, that VOs are sealed, that the registry builds, that `Version` is a concurrency token, and that every action has a covered test class.

## 5. Tests: `tests/AppTemplate.Api.Tests/Features/<Aggregates>/`

1. `<Aggregate>TestBase.cs`: `public abstract class <Aggregate>TestBase : ApiTestBase` with `protected override ApiFactory CreateFactory() => new(includeFixtures: false);` and arrange helpers that run real actions (`Create<Aggregate>(...)` returning the id). Copy `tests/AppTemplate.Api.Tests/Features/Projects/ProjectTestBase.cs`.
2. `Create<Aggregate>Tests.cs` and `Get<Aggregate>Tests.cs` with `[Covers]` for every case (`skill:tdd`). For a create, the stale case is a state change between preview and execute that makes the create fail, such as another create taking the same unique name: execute returns `preview.stale` (reason `domain_error`). See `Execute_SameNameCreatedSincePreview_ReturnsStale` in `tests/AppTemplate.Api.Tests/Features/Projects/CreateProjectTests.cs`. A create with no such rule still needs a `Case.Stale` test; executing its token twice returns `preview.already_executed`.
3. Optional VO unit tests in `tests/AppTemplate.Api.Tests/Domain/`.

## 6. Contract, UI, verify

```bash
scripts/update-openapi.sh
cd src/web && npm run gen:api
```

Add a page with `skill:frontend-feature`. Then `scripts/verify.sh` must print `VERIFY OK`.

## Done when

- [ ] `AggregateRoots_VersionIsConcurrencyToken` and `CoverageTests` pass and none of them is skipped.
- [ ] `PersistenceTests.Model_AfterMigrations_HasNoPendingChanges` passes.
- [ ] `scripts/verify.sh` printed `VERIFY OK`.
