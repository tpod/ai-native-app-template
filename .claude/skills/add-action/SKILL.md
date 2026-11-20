---
name: add-action
description: Use when adding a new action (mutation) or query to an existing aggregate, end to end from value object to UI. Generates the HTTP endpoints, MCP tools and preview flow from one class.
---

# Add an action or query

One `[Action]` or `[Query]` class in `src/AppTemplate.Api/Features/<Aggregate>/` becomes HTTP endpoints, MCP tools, OpenAPI operations and, for mutations, the preview → confirm → execute flow. Never hand-write an endpoint, controller, `MapPost` or `[McpServerTool]` for it. Do not use the kit's `/scaffold`.

Worked example: `rename_project`. Read its files side by side as you go:

| Step | Worked example |
|---|---|
| Value object | `src/AppTemplate.Domain/Projects/ProjectName.cs` |
| Aggregate behavior | `src/AppTemplate.Domain/Projects/Project.cs` (`Rename`) |
| Domain errors | `src/AppTemplate.Domain/Projects/ProjectErrors.cs` |
| Domain events | `src/AppTemplate.Domain/Projects/ProjectEvents.cs` |
| Validator | `src/AppTemplate.Api/Features/Projects/ProjectValidators.cs` (`RenameProjectInputValidator`) |
| Action class | `src/AppTemplate.Api/Features/Projects/RenameProject.cs` |
| DB-dependent lookup | `src/AppTemplate.Api/Features/Projects/ProjectLookup.cs` |
| EF mapping | `src/AppTemplate.Api/Data/ProjectConfiguration.cs` |
| Tests | `tests/AppTemplate.Api.Tests/Features/Projects/RenameProjectTests.cs` |
| UI | `src/web/src/pages/project-detail-page.tsx` |

If `skill:remove-example` has already deleted these files, read them from Git history: `git log --diff-filter=D --oneline -- src/AppTemplate.Api/Features/Projects/RenameProject.cs` gives the deleting commit; `git show <commit>^:src/AppTemplate.Api/Features/Projects/RenameProject.cs` prints the file.

A new aggregate root needs `skill:add-aggregate` first.

## Order

Write the test class (step 6) first and watch it fail (`skill:tdd`). Then build steps 1 to 5 in order, go green, and finish with steps 7 to 9.

### 1. Value objects (`skill:domain-modeling`)

One per input value with rules, in `src/AppTemplate.Domain/<Aggregate>/`. A `sealed record` with a private constructor, `static OneOf<TVo, ValidationError> Create(string? raw)` and, for a single value, `IValueObject<TSelf, TValue>` with `FromTrusted`. Field error codes are `<vo_snake>.<problem>`, for example `project_name.too_long`. Reuse an existing VO when the rules are the same.

### 2. Aggregate behavior and domain errors (`skill:domain-modeling`)

- Add a method on the aggregate that returns `OneOf<Success, E1, …>` with only the errors it can produce. Example: `Project.Rename(ProjectName name, ProjectId? currentHolderOfName)` returns `OneOf<Success, ProjectIsArchived, ProjectNameTaken>`.
- The method takes value objects, never raw input. Data that needs the database (uniqueness, existence of another row) is loaded by the action and passed in as a parameter.
- A no-op (same value) returns `Success` without changing state or raising an event, so the preview shows empty `changes` and `Version` does not move.
- Raise a past-tense event (`ProjectRenamed`) with `Raise(...)` whenever state changes, and give it `[Event("<aggregate>.<verb>", 1)]`. A state change without an event fails as `internal.unexpected`; the event becomes the history row an agent reads later.
- Each new error is a `sealed record` deriving from `AppError` with `[Error("area.snake_case", ErrorKind.X, "Title")]`, plus `Message`, `Suggestion` and optional `Details`.

### 3. Validator

Add `public sealed class <ActionClass>InputValidator : AbstractValidator<<ActionClass>Input>` to `src/AppTemplate.Api/Features/<Aggregate>/<Aggregate>Validators.cs`. AddActions registers every public validator in the Api assembly. The pipeline runs it before `Load` or `Run` and returns all failures as one `validation.failed`.

- VO-backed fields: `RuleFor(x => x.Name).MustBeValid(ProjectName.Create);`. The VO's code, message and suggestion flow through.
- Input-only rules: `RuleFor(x => x.ProjectId).NotEmpty().WithSuggestion("Use an id returned by list_projects or get_project.");`. The default field code is `<property_snake>.<validator_snake>` (`project_id.not_empty`); set your own with `.WithErrorCode("area.code")`.
- Never `ValidateAndThrow`, never `ValidationException`.
- A validator adds `validation.failed` to the action's declared error codes, so the tests must cover it.

### 4. The action or query class

One file per action in `src/AppTemplate.Api/Features/<Aggregate>/`, holding its input record and handler, both `public`. A mutation's input must not declare a `reason` property: `reason` is the optional execute field stored in history, and registration fails if an input shadows it.

**Mutation** (shape of `src/AppTemplate.Api/Features/Projects/RenameProject.cs`):

```csharp
public sealed record RenameProjectInput(Guid ProjectId, string Name);

[Action("rename_project", "Rename an active project. Names are unique among active projects, ignoring case and surrounding spaces. Warns project.unchanged when the name is the same.")]
public sealed class RenameProject : IMutation<RenameProjectInput, Project>
{
    public async Task<OneOf<Project, ProjectNotFound>> Load(RenameProjectInput input, ActionContext ctx) => …;
    public async Task<OneOf<Success, ValidationFailed, ProjectIsArchived, ProjectNameTaken>> Apply(Project target, RenameProjectInput input, ActionContext ctx) => …;
    public string Summarize(Project target, RenameProjectInput input) => $"Rename project '{target.Name.Value}' to '{input.Name.Trim()}'.";
    public object ToResult(Project target) => ProjectDto.From(target);
}
```

- Every input parameter without a default is required: omitting it, or sending `null` for a non-nullable one, returns `400 request.malformed` with the field path. Give optional fields a default (`ProjectStatus? Status = null`) so the MCP and OpenAPI schemas mark them optional.
- The name is snake_case (`^[a-z][a-z0-9_]*$`), unique, and never starts with `preview_` (reserved for the generated preview tools). The description is written for an agent: what it does, the rules it enforces, and any warning codes.
- `Load` and `Apply` are convention methods named exactly that. The OneOf error arguments of `Load` plus `Apply` are the action's declared error codes. They appear in the MCP tool description and OpenAPI, and `CoverageTests` requires a `[Covers]` test for each.
- `Load` finds the tracked aggregate (with `Include` for children) or returns `*NotFound`. A **create** builds the aggregate in `Load` instead, takes its id from `ctx.NewId()` and adds it to `ctx.Db`; `Apply` then returns `new Success()`. See `src/AppTemplate.Api/Features/Projects/CreateProject.cs`.
- Never use `AsNoTracking()` in `Load`. The change set comes from the change tracker, so the pipeline throws (`internal.unexpected`) when `Load` returns an untracked aggregate rather than silently saving nothing.
- `Apply` converts input to VOs (return `ValidationFailed.For("name", error)` if `Create` fails), loads DB-dependent data (`ProjectLookup.FindNameHolder`), calls the domain method and maps its OneOf with `.Match`. It never calls `SaveChanges`; the pipeline saves on execute only.
- Use `ctx.NewId()` for every new id (execute replays the preview's ids), `ctx.Time` for the clock, and `ctx.Warn("code", "message")` for non-blocking preview warnings.
- `Summarize` runs after `Load` and before `Apply`, so it must not assume `Apply` succeeded.
- `ToResult` returns the execute response body, usually the aggregate DTO.
- Destructive mutations set `Destructive = true` (see `src/AppTemplate.Api/Features/Projects/ArchiveProject.cs`). `PreviewLifetimeSeconds` overrides the 300 s token lifetime.

**Query** (shape of `src/AppTemplate.Api/Features/Projects/GetProject.cs` and `src/AppTemplate.Api/Features/Projects/ListProjects.cs`): `[Query("get_x", "…")]` on a class implementing `IQuery<TInput, TOutput>` with `public async Task<OneOf<TOutput, E1, …>> Run(TInput input, ActionContext ctx)`. Read with `AsNoTracking()` and project with `.Select(...)` into DTOs.

The registry checks every rule above when the app starts and throws `ActionRegistrationException` listing each violation. `Registry_ApiAssembly_BuildsWithoutViolations` in `tests/AppTemplate.Architecture.Tests` runs the same check.

### 5. EF mapping and migration (`skill:ef-core`)

Only when the persisted shape changed (new property, child entity or index). Edit the aggregate's `IEntityTypeConfiguration<T>` in `src/AppTemplate.Api/Data/`, then:

```bash
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> -p src/AppTemplate.Api -o Data/Migrations
```

`PersistenceTests.Model_AfterMigrations_HasNoPendingChanges` fails if you forget the migration.

### 6. Integration test class (`skill:tdd`)

`tests/AppTemplate.Api.Tests/Features/<Aggregate>/<ActionClass>Tests.cs`, deriving from the aggregate's test base, with `[Covers]` for `Case.HappyPath`, each declared error code (including `validation.failed`) and, for mutations, `Case.Stale`.

### 7. Contract artifacts

```bash
scripts/update-openapi.sh
cd src/web && npm run gen:api
```

Commit `src/web/openapi.json` and `src/web/src/api/schema.d.ts` with the action. Changing an existing action's input, output or error codes is a contract change: follow `skill:change-action-contract`.

### 8. Optional UI (`skill:frontend-feature`)

### 9. Verify

`scripts/verify.sh` must print `VERIFY OK`. Then try the tool as a runtime agent would: run `dotnet run --project src/AppTemplate.AppHost` and call `preview_<name>` and `<name>` through the `app` MCP server.

## Done when

- [ ] The test class covers every declared code, and `CoverageTests` passes.
- [ ] No endpoint, controller or MCP tool was hand-written.
- [ ] `src/web/openapi.json` and `src/web/src/api/schema.d.ts` are regenerated and committed.
- [ ] `scripts/verify.sh` printed `VERIFY OK`.
