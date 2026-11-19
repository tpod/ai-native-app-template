# AI-Native App Template Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a clone-ready .NET 10 + React template in which every action is defined once and becomes an HTTP endpoint, MCP tool(s) and a bound preview, with structured errors, strong observability and coding-agent tooling that keeps new features in the same shape.

**Architecture:** A pure DDD `Domain` project (aggregates, value objects, OneOf results, `AppError` types). An `Api` project with:
- an action registry that discovers `[Action]`/`[Query]` classes and serves them over HTTP and MCP through one `ActionPipeline`;
- the preview engine, which reads EF `ChangeTracker` diffs and signs stateless HMAC tokens;
- EF Core on SQLite.

Aspire orchestrates the Api, the SQLite resource and the Vite dev server. The React UI calls the same actions through a generated OpenAPI client.

**Tech Stack:** .NET 10, C#, Aspire 17, EF Core 10 (SQLite), OneOf, FluentValidation, ModelContextProtocol.AspNetCore, OpenTelemetry, xUnit v3, Shouldly, NetArchTest.eNhancedEdition, React + Vite + TypeScript, Tailwind CSS v4, shadcn/ui, TanStack Table/Query, React Router, openapi-typescript + openapi-fetch.

**Spec:** `docs/superpowers/specs/2026-11-19-ai-native-app-template-design.md`. Read it alongside this plan; section refs (§) point there.

## Global Constraints

- SDK: .NET 10 (`10.0.115` installed); `global.json` pins `10.0.100` with `rollForward: latestFeature`. Node 22. No Docker.
- `Directory.Build.props`: `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest`, `EnforceCodeStyleInBuild=true`.
- Central package management: every `PackageReference` is versionless; versions live only in `Directory.Packages.props`.
- **Before using any third-party API, read the installed definition.** This covers MCP SDK handlers, Aspire hosting APIs, FluentValidation internals and EF ChangeTracker members. Grep the restored package in `~/.nuget/packages`, or use the Microsoft Learn/Context7 docs. Don't guess signatures.
- `AppTemplate.Domain` references only `OneOf`.
- Error codes match `^[a-z_]+\.[a-z_]+$`.
- HTTP status by `ErrorKind`:

  | `ErrorKind` | Status |
  |---|---|
  | `Validation` | 422 |
  | `NotFound` | 404 |
  | `Conflict` | 409 |
  | `Precondition` | 409 |
  | `BadRequest` | 400 |
  | `Unexpected` | 500 |

- Every error response is `application/problem+json`. It has `type`, `title`, `status`, `detail`, `instance`, and the extensions `code`, `details`, `suggestion` and `traceId`.
- Every HTTP response carries an `X-Trace-Id` header.
- Preview token: HMAC-SHA256. The default lifetime is 300 s. The key comes from config `Preview:SigningKey` and must be ≥ 32 UTF-8 bytes. The fixed test key is `test-signing-key-0123456789abcdef-0123`.
- Telemetry: `ActivitySource` and `Meter` are both named `AppTemplate.Actions`.
- Span name: `action {name} {phase}`, with phase ∈ `preview|execute|query`.
- Span tags: `action.name`, `action.phase`, `action.outcome` (`success` or the error code), `aggregate.type`, `aggregate.id`, `preview.stale_reason`.
- Tests:
  - xUnit v3, Shouldly only. No `Assert.*` and no FluentAssertions.
  - Name tests `Method_State_Expected`.
  - Pass `TestContext.Current.CancellationToken` wherever xUnit analyzer xUnit1051 asks for it. Warnings are errors.
- EF `InMemory` provider, Mediator/MediatR, `ValidateAndThrow`, `ValidationException` and any `Result`/`Result<T>` type are banned.
- JSON: `camelCase` properties, and enums as `snake_case_lower` strings via `JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)`.
- Commits: Conventional Commits, one logical change each. End every message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`. Never push.
- Never run `aspire agent init` or `/dotnet-init`.

## Review Focus

Most likely failure first. Each item names the task whose tests pin it.

1. **The same preview token executed twice** (an agent retrying, or a double-click). An update's second execute returns `preview.stale`, and a create's returns `preview.already_executed`. A change is never applied twice. *Task 9.*
2. **Wrong JSON shapes in input:** a non-GUID string for a `Guid`, an array body, an empty body. These return `400 request.malformed` with the JSON path, never a 500. *Task 9.*
3. **Unknown paths under `/api/` or `/mcp`.** These must return a ProblemDetails `404 request.not_found`, not the SPA's `index.html`. *Tasks 5 and 15.*
4. **Project names that differ only in case or surrounding whitespace** (`"Alpha"` vs `" alpha "`). These collide and return `project.name_taken`. *Task 13.*
5. **No-op mutations** (renaming a project to its current name). The preview shows empty `changes`. Execute succeeds without bumping `Version`, so other open previews don't go stale. *Tasks 9 and 13.*

---

## File Structure

```
global.json  AppTemplate.slnx  Directory.Build.props  Directory.Packages.props  .editorconfig  .gitignore
.config/dotnet-tools.json                      # dotnet-ef local tool
AGENTS.md  CLAUDE.md  .mcp.json
.claude/settings.json                          # plugin marketplaces + enabled plugins
.claude/rules/*.md                             # copied from dotnet-claude-kit v0.14.0; 3 edited
.claude/skills/<11 repo skills>/SKILL.md, .claude/skills/shadcn/ (vendored)
scripts/verify.sh  scripts/rename.sh  scripts/update-openapi.sh
src/AppTemplate.Domain/
  Common/  AppError.cs ErrorAttribute.cs ErrorKind.cs IAppError.cs ValidationError.cs ValidationFailed.cs
           IValueObject.cs Entity.cs AggregateRoot.cs IAggregateRoot.cs IDomainEvent.cs
  Projects/ ProjectId.cs TaskItemId.cs ProjectName.cs TaskTitle.cs ProjectStatus.cs Project.cs TaskItem.cs
            ProjectErrors.cs ProjectEvents.cs
src/AppTemplate.Api/
  Program.cs  appsettings.json  appsettings.Development.json  Properties/launchSettings.json
  Actions/  ActionAttribute.cs QueryAttribute.cs IMutation.cs IQuery.cs ActionContext.cs ActionDescriptor.cs
            ActionRegistry.cs ActionRegistryOptions.cs ActionRegistrationException.cs ActionPipeline.cs
            ActionServiceCollectionExtensions.cs FrameworkErrors.cs Responses.cs
            Preview/ ChangeSetReader.cs ChangeSetHasher.cs InputHasher.cs PreviewTokenSigner.cs PreviewOptions.cs
            Validation/ ValidationRuleExtensions.cs ValidationFailedMapper.cs
            Events/ IDomainEventHandler.cs DomainEventDispatcher.cs
            Http/ ActionEndpoints.cs AppErrorProblemDetails.cs ProblemDetailsSetup.cs TraceIdMiddleware.cs OpenApiSetup.cs
            Telemetry/ ActionTelemetry.cs ActionMetrics.cs ActionLog.cs
  Mcp/      McpSetup.cs McpToolFactory.cs ToolDescriptions.cs McpInstructions.cs
  Data/     AppDbContext.cs IModelContributor.cs ValueObjectConventions.cs AppDbContextDesignTimeFactory.cs
            Configurations/ProjectConfiguration.cs Configurations/TaskItemConfiguration.cs Migrations/
  Features/Projects/ CreateProject.cs RenameProject.cs AddTask.cs CompleteTask.cs ArchiveProject.cs
                     ListProjects.cs GetProject.cs ProjectDtos.cs ProjectValidators.cs
  wwwroot/ (gitignored build output)
src/AppTemplate.ServiceDefaults/ Extensions.cs
src/AppTemplate.AppHost/ AppHost.cs
src/web/ (Vite app; see Tasks 15–16)
tests/AppTemplate.Api.Tests/
  Infrastructure/ ApiFactory.cs ApiTestBase.cs ApiResponse.cs ApiResponseShouldly.cs CoversAttribute.cs
                  TestOutputLoggerProvider.cs ActivityCapture.cs SqliteTestDatabase.cs
  Fixtures/Notes/  (test-only aggregate + actions, Task 8)
  Domain/  Data/  Actions/  Mcp/  OpenApi/  Telemetry/  Features/Projects/
tests/AppTemplate.Architecture.Tests/ ArchitectureTests.cs CoverageTests.cs ErrorCodeTests.cs SkillLintTests.cs
```

---

### Task 1: Repository skeleton and build settings

**Files:**
- Create:
  - `global.json`, `AppTemplate.slnx`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, `.config/dotnet-tools.json`
  - `src/AppTemplate.Domain/AppTemplate.Domain.csproj`
  - `src/AppTemplate.Api/AppTemplate.Api.csproj`, `Program.cs`, `appsettings*.json`, `Properties/launchSettings.json`
  - `src/AppTemplate.ServiceDefaults/*` and `src/AppTemplate.AppHost/*`, generated by the templates
  - `tests/AppTemplate.Api.Tests/AppTemplate.Api.Tests.csproj` and `tests/AppTemplate.Architecture.Tests/AppTemplate.Architecture.Tests.csproj`

**Interfaces:**
- Produces:
  - project names, the namespace root `AppTemplate`, and `public partial class Program;` at the end of `Program.cs`;
  - the Api launch profile `http`, on `http://localhost:5180`.

- [ ] **Step 1: Install the templates and confirm versions.**
  - Run `dotnet new install Aspire.ProjectTemplates` and `dotnet new install xunit.v3.templates`.
  - Run `dotnet new list aspire` and `dotnet new list xunit3`.
  - Expected: `aspire-apphost`, `aspire-servicedefaults` and `xunit3` are listed. Note the Aspire template version, which must be 17.x. If it isn't, run `dotnet new install Aspire.ProjectTemplates::17.*`.
- [ ] **Step 2: Create the projects from the templates.**
  - `dotnet new aspire-apphost -o src/AppTemplate.AppHost`
  - `dotnet new aspire-servicedefaults -o src/AppTemplate.ServiceDefaults`
  - `dotnet new classlib -o src/AppTemplate.Domain`
  - `dotnet new web -o src/AppTemplate.Api`
  - `dotnet new xunit3` for both test projects
  - `dotnet new sln --format slnx -n AppTemplate`, then add all six projects.
  - References:
    - Api → Domain, ServiceDefaults.
    - AppHost → Api.
    - Api.Tests → Api.
    - Architecture.Tests → Api, Domain, Api.Tests.
  - Delete template boilerplate such as `Class1.cs` and the sample test.
- [ ] **Step 3: Move every package version into `Directory.Packages.props`, with `ManagePackageVersionsCentrally=true`.**
  - Add these packages now, at their latest stable versions, Aspire-aligned where relevant:
    - `OneOf`, `FluentValidation`, `FluentValidation.DependencyInjectionExtensions`
    - `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`
    - `Microsoft.AspNetCore.OpenApi`
    - `ModelContextProtocol.AspNetCore`, `ModelContextProtocol`
    - `Shouldly`
    - `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.Extensions.TimeProvider.Testing`
    - `NetArchTest.eNhancedEdition`
    - `CommunityToolkit.Aspire.Hosting.SQLite`, `Aspire.Hosting.JavaScript`
    - `OpenTelemetry.Instrumentation.EntityFrameworkCore`
  - Plus the packages the templates added.
  - Confirm on NuGet that the CommunityToolkit and JavaScript hosting packages list Aspire 17 compatibility.
- [ ] **Step 4: Write `Directory.Build.props`** with the values from Global Constraints. Write `.editorconfig` too:
  - `[**/Migrations/*.cs] generated_code = true`;
  - standard C# style;
  - `dotnet_diagnostic.CA1707.severity = none` for `tests/**`, so test names can use underscores.
- [ ] **Step 5: Write `global.json`** (`{"sdk":{"version":"10.0.100","rollForward":"latestFeature"}}`). Create the `dotnet-ef` local tool with `dotnet new tool-manifest && dotnet tool install dotnet-ef`. Write `.gitignore` (VisualStudio template + `src/AppTemplate.Api/wwwroot/`, `*.db`, `node_modules/`).
- [ ] **Step 5b: Add `<InternalsVisibleTo Include="AppTemplate.Api.Tests" />`** to the Api csproj. Tests construct `ActionContext` and call internal invokers.
- [ ] **Step 6: Wire `Program.cs`:**
  - `builder.AddServiceDefaults()`, `app.MapDefaultEndpoints()`, and the `public partial class Program;` declaration.
  - Set `launchSettings.json` profile `http` to `http://localhost:5180`.
- [ ] **Step 7: Verify the build.**
  - Run `dotnet build AppTemplate.slnx`. Expected: `Build succeeded`, `0 Warning(s)`.
  - Run `dotnet test AppTemplate.slnx`. Expected: exit 0. No tests is acceptable at this stage.
- [ ] **Step 8: Commit** with `chore: scaffold solution, build settings and central package management`.

---

### Task 2: Domain kernel

**Files:**
- Create: `src/AppTemplate.Domain/Common/*.cs` (see File Structure)
- Test: `tests/AppTemplate.Api.Tests/Domain/AppErrorTests.cs`, `tests/AppTemplate.Api.Tests/Domain/AggregateRootTests.cs`

**Interfaces:**
- Produces (namespace `AppTemplate.Domain.Common`):
  - `enum ErrorKind { Validation, NotFound, Conflict, Precondition, BadRequest, Unexpected }`
  - `interface IAppError { string Code { get; } string Title { get; } ErrorKind Kind { get; } string Message { get; } object? Details { get; } string Suggestion { get; } }`
  - `[AttributeUsage(AttributeTargets.Class, Inherited = false)] sealed class ErrorAttribute(string code, ErrorKind kind, string title) : Attribute` with properties `Code`, `Kind`, `Title`.
  - `static class ErrorMetadata { static ErrorAttribute For(Type errorType); }`
    - Cached.
    - Throws `InvalidOperationException("<FullName> must declare [Error(code, kind, title)]")`.
  - `abstract record AppError : IAppError`
    - `Code`, `Title` and `Kind` come from `ErrorMetadata.For(GetType())`.
    - `abstract string Message`, `abstract string Suggestion`, `virtual object? Details => null`.
  - `sealed record ValidationError(string Code, string Message, string Suggestion)` is a field-level error returned by value-object `Create`. It is **not** an `AppError`.
  - `sealed record FieldFailure(string Field, string Code, string Message, string Suggestion)`
  - `[Error("validation.failed", ErrorKind.Validation, "Validation failed")] sealed record ValidationFailed(IReadOnlyList<FieldFailure> Fields) : AppError`
    - `Message`: `"{n} field(s) failed validation: {comma-joined fields}"`.
    - `Details`: `new { fields = Fields }`.
    - `Suggestion`: `"Fix each field listed in details.fields (each has its own suggestion) and call the tool again."`
    - `static ValidationFailed For(string field, ValidationError error)`.
  - `interface IValueObject<TSelf, TValue> where TSelf : IValueObject<TSelf, TValue> { TValue Value { get; } static abstract TSelf FromTrusted(TValue value); }`
    - `FromTrusted` is for persistence and serialization only.
  - `interface IDomainEvent;`
  - `interface IAggregateRoot { int Version { get; } IReadOnlyList<IDomainEvent> DomainEvents { get; } void ClearDomainEvents(); void IncrementVersion(); }`
  - `abstract class Entity<TId> where TId : struct { TId Id { get; protected init; } }`
  - `abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot where TId : struct`
    - `Version { get; private set; }`.
    - `protected void Raise(IDomainEvent e)`.
    - `IncrementVersion` is implemented explicitly.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Error("test.sample", ErrorKind.Conflict, "Sample title")]
private sealed record SampleError(string Name) : AppError
{ public override string Message => $"m {Name}"; public override string Suggestion => "s"; }
private sealed record Undeclared : AppError { public override string Message => ""; public override string Suggestion => ""; }

[Fact] public void Code_ErrorWithAttribute_ReadsCodeKindTitle()
{ var e = new SampleError("x"); e.Code.ShouldBe("test.sample"); e.Kind.ShouldBe(ErrorKind.Conflict); e.Title.ShouldBe("Sample title"); e.Message.ShouldBe("m x"); }
[Fact] public void Code_ErrorWithoutAttribute_ThrowsNamingType()
=> Should.Throw<InvalidOperationException>(() => new Undeclared().Code).Message.ShouldContain(nameof(Undeclared));
[Fact] public void For_FieldError_BuildsSingleFieldFailure()
{ var v = ValidationFailed.For("name", new ValidationError("project_name.empty", "Name is empty", "Provide a name"));
  v.Code.ShouldBe("validation.failed"); v.Fields.ShouldHaveSingleItem().ShouldBe(new FieldFailure("name", "project_name.empty", "Name is empty", "Provide a name")); }
```

`AggregateRootTests`, using a private test aggregate with a public `DoThing()` that calls `Raise`:
- `Raise_ThenClear_EmptiesEvents`: one event, then zero after `ClearDomainEvents()`.
- `IncrementVersion_Twice_VersionIsTwo`.
- [ ] **Step 2: Run** `dotnet test tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~Domain"`. Expected: build fails, because the types don't exist yet.
- [ ] **Step 3: Implement the kernel types.** `ErrorMetadata` caches results in a `ConcurrentDictionary<Type, ErrorAttribute>`.
- [ ] **Step 4: Run the same filter.** Expected: PASS (5 tests).
- [ ] **Step 5: Commit** with `feat(domain): add error, value object and aggregate kernel`.

---

### Task 3: Projects domain model

**Files:**
- Create: `src/AppTemplate.Domain/Projects/*.cs`
- Test: `tests/AppTemplate.Api.Tests/Domain/ProjectNameTests.cs`, `TaskTitleTests.cs`, `ProjectTests.cs`

**Interfaces:**
- Consumes: Task 2 kernel.
- Produces (namespace `AppTemplate.Domain.Projects`):
  - Ids: `readonly record struct ProjectId : IValueObject<ProjectId, Guid>` and `readonly record struct TaskItemId : IValueObject<TaskItemId, Guid>`. Each has `Value`, `static From(Guid)` and `static FromTrusted(Guid)`, plus a private constructor.
  - `sealed record ProjectName : IValueObject<ProjectName, string>`
    - `const int MaxLength = 100`.
    - `static OneOf<ProjectName, ValidationError> Create(string? raw)` trims the input first.
    - Codes: `project_name.empty` and `project_name.too_long`.
  - `sealed record TaskTitle : IValueObject<TaskTitle, string>`
    - `const int MaxLength = 200`.
    - Codes: `task_title.empty` and `task_title.too_long`.
  - `enum ProjectStatus { Active, Archived }`
  - `sealed class TaskItem : Entity<TaskItemId>` with `TaskTitle Title` and `bool Done`. Its constructor and `Complete()` are internal.
  - `sealed class Project : AggregateRoot<ProjectId>`:
    - Properties: `ProjectName Name`, `ProjectStatus Status`, `IReadOnlyList<TaskItem> Tasks`, backed by a `List<TaskItem> _tasks` field.
    - `static OneOf<Project, ProjectNameTaken> Create(ProjectId id, ProjectName name, ProjectId? currentHolderOfName)`
    - `OneOf<Success, ProjectIsArchived, ProjectNameTaken> Rename(ProjectName name, ProjectId? currentHolderOfName)`
    - `OneOf<Success, ProjectIsArchived> AddTask(TaskItemId id, TaskTitle title)`
    - `OneOf<Success, ProjectIsArchived, TaskNotFound, TaskAlreadyDone> CompleteTask(TaskItemId id)`
    - `OneOf<Success, ProjectIsArchived> Archive()`
    - `currentHolderOfName` is the id of the active project that holds that name, or null. A holder equal to `Id` is not a conflict.
  - Errors in `ProjectErrors.cs`:

    | Record | Code | Kind |
    |---|---|---|
    | `ProjectNotFound(ProjectId ProjectId)` | `project.not_found` | `NotFound` |
    | `ProjectNameTaken(string Name, ProjectId ExistingProjectId)` | `project.name_taken` | `Conflict` |
    | `ProjectIsArchived(ProjectId ProjectId)` | `project.archived` | `Conflict` |
    | `TaskNotFound(ProjectId ProjectId, TaskItemId TaskId)` | `task.not_found` | `NotFound` |
    | `TaskAlreadyDone(ProjectId ProjectId, TaskItemId TaskId)` | `task.already_done` | `Conflict` |

    `Details` uses primitive Guid and string values. Suggestions name tools: for example, `ProjectNameTaken` → `"Choose a different name; list_projects shows existing names."`, and `ProjectNotFound` → `"Check the id; list_projects returns valid project ids."`
  - Events in `ProjectEvents.cs` (all `IDomainEvent`, with primitive payloads):
    - `ProjectCreated(Guid ProjectId, string Name)`
    - `ProjectRenamed(Guid ProjectId, string OldName, string NewName)`
    - `TaskAdded(Guid ProjectId, Guid TaskId, string Title)`
    - `TaskCompleted(Guid ProjectId, Guid TaskId)`
    - `ProjectArchived(Guid ProjectId)`

- [ ] **Step 1: Write the failing tests.**
  - `ProjectNameTests`:
    - `Create_PaddedName_Trims`: `"  Alpha  "` gives `"Alpha"`.
    - `Create_Whitespace_ReturnsEmpty`: code `project_name.empty`.
    - `Create_100Chars_Succeeds`.
    - `Create_101Chars_ReturnsTooLong`: code `project_name.too_long`, and the message contains `100`.
  - `TaskTitleTests`: the same four cases with 200 and 201 characters and the `task_title.*` codes.
  - `ProjectTests`:
    - `Create_NameFree_RaisesProjectCreated`: Status `Active`, one `ProjectCreated` event.
    - `Create_NameHeldByOther_ReturnsNameTaken`: `ExistingProjectId` equals the holder.
    - `Rename_SameName_SucceedsWithoutEvent`: `DomainEvents` is empty after a clear.
    - `Rename_NameHeldBySelf_Succeeds`.
    - `Rename_Archived_ReturnsArchived`.
    - `Rename_NewName_RaisesProjectRenamed`: `OldName` and `NewName` are set.
    - `AddTask_Archived_ReturnsArchived`.
    - `CompleteTask_Unknown_ReturnsTaskNotFound`.
    - `CompleteTask_Twice_ReturnsAlreadyDone`.
    - `Archive_Twice_ReturnsArchived`.
    - `Archive_Active_RaisesProjectArchived`.
  - Assert result branches with `result.IsT1.ShouldBeTrue()` and `result.AsT1.ShouldBeOfType<…>()`.
- [ ] **Step 2: Run** `dotnet test tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~Domain"`. Expected: the build fails.
- [ ] **Step 3: Implement.**
  - `Project` has a private parameterless constructor for EF.
  - Each mutation clears nothing. Callers own the event lifecycle.
- [ ] **Step 4: Run the same filter.** Expected: PASS.
- [ ] **Step 5: Commit** with `feat(domain): add Projects aggregate, value objects, errors and events`.

---

### Task 4: Data layer and initial migration

**Files:**
- Create: `src/AppTemplate.Api/Data/*` (see File Structure) and `tests/AppTemplate.Api.Tests/Infrastructure/SqliteTestDatabase.cs`
- Modify: `src/AppTemplate.Api/Program.cs`
- Test: `tests/AppTemplate.Api.Tests/Data/PersistenceTests.cs`

**Interfaces:**
- Consumes: Task 3 types.
- Produces:
  - `sealed class AppDbContext(DbContextOptions<AppDbContext> options, IEnumerable<IModelContributor> contributors) : DbContext` with `DbSet<Project> Projects`.
    - It applies the configurations from its own assembly, then each contributor.
    - A custom `IModelCacheKeyFactory` keys the model cache by the contributor types.
  - `interface IModelContributor { void Configure(ModelBuilder modelBuilder); string SchemaSql { get; } }`
  - `static class ValueObjectConventions { static void Apply(ModelConfigurationBuilder builder, Assembly domainAssembly); }`
    - Registers a converter, `Value` ↔ `FromTrusted`, for every type implementing `IValueObject<,>`.
  - Mapping:
    - `ProjectName` uses `UseCollation("NOCASE")`, which is SQLite-specific and documented in `swap-to-postgres`.
    - `Version` is `IsConcurrencyToken()`.
    - `Status` is stored as a string.
    - Tasks use table `TaskItems`, with a shadow FK `ProjectId` and field access to `_tasks`.
    - There is an index on `(Name, Status)`.
  - `AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>`, using `Data Source=design.db`.
  - In `Program`:
    - `AddDbContext<AppDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("appdb")))`.
    - In Development only, `db.Database.Migrate()` at startup.
    - `appsettings.Development.json` sets `ConnectionStrings:appdb = Data Source=apptemplate.db`.
  - Test helper `SqliteTestDatabase : IDisposable`:
    - Opens a `SqliteConnection("DataSource=:memory:")`.
    - `AppDbContext NewContext(params IModelContributor[] contributors)`.
    - `void Migrate()` runs `Database.Migrate()` and then each contributor's `SchemaSql`.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public async Task SaveChanges_ProjectWithTasks_RoundTripsThroughFreshContext()
{ using var db = new SqliteTestDatabase(); db.Migrate();
  var id = ProjectId.From(Guid.NewGuid());
  var p = Project.Create(id, ProjectName.Create("Alpha").AsT0, null).AsT0;
  p.AddTask(TaskItemId.From(Guid.NewGuid()), TaskTitle.Create("T1").AsT0);
  await using (var c = db.NewContext()) { c.Projects.Add(p); await c.SaveChangesAsync(TestContext.Current.CancellationToken); }
  await using var c2 = db.NewContext();
  var loaded = await c2.Projects.Include(x => x.Tasks).SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);
  loaded.Name.Value.ShouldBe("Alpha"); loaded.Tasks.ShouldHaveSingleItem().Title.Value.ShouldBe("T1"); }
```

  - `SaveChanges_ConcurrentVersionBump_SecondThrowsConcurrency`:
    - Two contexts load the same project.
    - Each calls `((IAggregateRoot)p).IncrementVersion()`.
    - The first save succeeds; the second throws `DbUpdateConcurrencyException`.
  - `Query_NameDiffersOnlyInCase_Matches`: after saving `"Alpha"`, `Projects.AnyAsync(p => p.Name == ProjectName.Create("alpha").AsT0)` is true.
  - `Model_AfterMigrations_HasNoPendingChanges`: `c.Database.HasPendingModelChanges().ShouldBeFalse()`.
- [ ] **Step 2: Run** `dotnet test tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~Data"`. Expected: the build fails.
- [ ] **Step 3: Implement the context, conventions, configurations and design-time factory.**
- [ ] **Step 4: Create the migration** with `dotnet tool run dotnet-ef migrations add InitialCreate -p src/AppTemplate.Api -o Data/Migrations`. Expected: `Done.`
- [ ] **Step 5: Run the same filter.** Expected: PASS (4 tests).
- [ ] **Step 6: Commit** with `feat(data): add AppDbContext, value object conventions and initial SQLite migration`.

---

### Task 5: Test host, ProblemDetails baseline and trace header

**Files:**
- Create:
  - `src/AppTemplate.Api/Actions/Http/AppErrorProblemDetails.cs`, `ProblemDetailsSetup.cs`, `TraceIdMiddleware.cs`
  - `src/AppTemplate.Api/Actions/FrameworkErrors.cs`
  - `tests/AppTemplate.Api.Tests/Infrastructure/*`, everything except `SqliteTestDatabase`
- Modify: `Program.cs`
- Test: `tests/AppTemplate.Api.Tests/Http/ProblemDetailsTests.cs`

**Interfaces:**
- Produces:
  - `FrameworkErrors.cs`: each record is an `AppError` with an `[Error]` attribute:

    | Record | Code | Kind |
    |---|---|---|
    | `PreviewTokenInvalid()` | `preview.token_invalid` | `BadRequest` |
    | `PreviewTokenMismatch(string Expected, string Actual)` | `preview.token_mismatch` | `Precondition` |
    | `PreviewExpired(DateTimeOffset ExpiredAt)` | `preview.expired` | `Precondition` |
    | `PreviewStale(string Action, string Reason, object? Cause)` | `preview.stale` | `Precondition` |
    | `PreviewAlreadyExecuted(string Action)` | `preview.already_executed` | `Conflict` |
    | `RequestMalformed(string Detail, string? Path)` | `request.malformed` | `BadRequest` |
    | `RequestNotFound(string Path)` | `request.not_found` | `NotFound` |
    | `InternalUnexpected()` | `internal.unexpected` | `Unexpected` |

    - `PreviewStale.Suggestion` is `$"Call preview_{Action} again and review the new preview."`
    - `InternalUnexpected.Suggestion` is `"This is not caused by your input. Report the traceId; retrying may not help."`
  - `static class AppErrorProblemDetails`:
    - `int StatusFor(ErrorKind kind)`
    - `ProblemDetails ToProblemDetails(IAppError error, string traceId, string instance)`
      - `type` = `https://errors.apptemplate.dev/{code}`
      - `title` = `Title`, `detail` = `Message`
      - extensions `code`, `details`, `suggestion`, `traceId`
    - `IResult ToResult(IAppError error, HttpContext ctx)`
  - `static IServiceCollection AddAppProblemDetails(this IServiceCollection services)`:
    - `AddProblemDetails` with a `CustomizeProblemDetails` that adds `traceId` everywhere, plus `code` and `suggestion` when they're missing (status 404 → `request.not_found`, everything else → `request.malformed`).
    - An exception handler maps unhandled exceptions to `InternalUnexpected`, logs the exception, and never puts exception text in the body.
  - `TraceIdMiddleware` sets the response header `X-Trace-Id` to `Activity.Current?.TraceId` or `HttpContext.TraceIdentifier`.
  - `Program` adds `app.MapFallback("/api/{**path}", …)` and `app.MapFallback("/mcp/{**path}", …)`, both returning `RequestNotFound`.
  - `ApiFactory : WebApplicationFactory<Program>, IAsyncDisposable`:
    - Environment `Testing`.
    - Owns a `SqliteTestDatabase`, replaces the `AppDbContext` options with its connection, and runs `Migrate()` after the host builds.
    - `FakeTimeProvider Time`, registered as `TimeProvider`.
    - Sets `Preview:SigningKey` to the test key.
    - `ActivityCapture Activities`.
    - Logs to `TestContext.Current.TestOutputHelper` through `TestOutputLoggerProvider`.
    - `Action<IServiceCollection>? ConfigureTestServices` lets later tasks add services.
  - `abstract class ApiTestBase : IAsyncLifetime` with `ApiFactory Factory` and `HttpClient Client`. Request helpers are added in Task 9.
  - `sealed record ApiResponse(HttpStatusCode Status, JsonElement Body, HttpResponseHeaders Headers)`:
    - `string TraceId`, read from the `X-Trace-Id` header.
    - `static async Task<ApiResponse> From(HttpResponseMessage)`.
    - `ApiTestBase` helpers write `Factory.Activities.Dump(response.TraceId)` and the body to test output after every request. xUnit shows that output when the test fails.
  - `static class ApiResponseShouldly`:
    - `ShouldFailWith(this ApiResponse r, string code, HttpStatusCode? status = null)` asserts the `application/problem+json` shape, all four extensions, and a non-empty `traceId`.
    - `ShouldHaveFieldError(this ApiResponse r, string field, string code)`.
    - `ShouldSucceed(this ApiResponse r)` gives a 2xx, and otherwise prints the body.
  - `[AttributeUsage(Method, AllowMultiple = true)] sealed class CoversAttribute` with two constructors, `(string action, string errorCode)` and `(string action, Case @case)`, and `enum Case { HappyPath, Stale }`.
  - `ActivityCapture`:
    - An `ActivityListener` on the sources `AppTemplate.Actions` and `Microsoft.AspNetCore`.
    - `IReadOnlyList<Activity> ForTrace(string traceId)`.
    - `string Dump(string traceId)`.

- [ ] **Step 1: Write the failing tests.** `ProblemDetailsTests : ApiTestBase`:

```csharp
[Fact] public async Task Get_UnknownApiRoute_Returns404ProblemWithCode()
{ var r = await ApiResponse.From(await Client.GetAsync("/api/nope", TestContext.Current.CancellationToken));
  r.ShouldFailWith("request.not_found", HttpStatusCode.NotFound); }
[Fact] public async Task Get_UnknownMcpSubRoute_Returns404Problem() { /* GET /mcp/nope → request.not_found */ }
[Fact] public async Task Get_Health_HasTraceIdHeader() { /* GET /health → 200, Headers contains X-Trace-Id non-empty */ }
[Fact] public void StatusFor_EachKind_MatchesTable()
{ AppErrorProblemDetails.StatusFor(ErrorKind.Validation).ShouldBe(422); /* … NotFound 404, Conflict 409, Precondition 409, BadRequest 400, Unexpected 500 */ }
```

- [ ] **Step 2: Run** `dotnet test tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~ProblemDetailsTests"`. Expected: FAIL.
- [ ] **Step 3: Implement the files above.** Register them in `Program`: `AddAppProblemDetails`, `UseExceptionHandler`, `UseStatusCodePages`, the middleware, and the fallbacks.
- [ ] **Step 4: Run the same filter.** Expected: PASS (4 tests).
- [ ] **Step 5: Commit** with `feat(api): add ProblemDetails mapping, trace header and integration test host`.

---

### Task 6: Action contracts and registry

**Files:**
- Create: `src/AppTemplate.Api/Actions/{ActionAttribute,QueryAttribute,IMutation,IQuery,ActionContext,ActionDescriptor,ActionRegistry,ActionRegistryOptions,ActionRegistrationException}.cs`
- Test: `tests/AppTemplate.Api.Tests/Actions/ActionRegistryTests.cs`, with the sample handler types nested in the test class

**Interfaces:**
- Consumes: `AppError`, `IAggregateRoot`, `AppDbContext`.
- Produces:
  - `[AttributeUsage(Class)] sealed class ActionAttribute(string name, string description) : Attribute` with `bool Destructive { get; init; }` and `int PreviewLifetimeSeconds { get; init; } = 300`.
  - `[AttributeUsage(Class)] sealed class QueryAttribute(string name, string description) : Attribute`
  - `interface IMutation<TInput, TAggregate> where TAggregate : class, IAggregateRoot` with:
    - `string Summarize(TAggregate target, TInput input)`
    - `object ToResult(TAggregate target)`
    - Two required **convention methods**, documented in its XML doc:
      - `Task<OneOf<TAggregate, E1, …>> Load(TInput input, ActionContext ctx)` loads or creates the aggregate. A create adds it to `ctx.Db`.
      - `Task<OneOf<Success, E1, …>> Apply(TAggregate target, TInput input, ActionContext ctx)`
  - `interface IQuery<TInput, TOutput>` with the convention method `Task<OneOf<TOutput, E1, …>> Run(TInput input, ActionContext ctx)`.
  - `sealed class ActionContext`:
    - `AppDbContext Db`, `TimeProvider Time`, `CancellationToken CancellationToken`
    - `Guid NewId()` returns the next replayed id when one is available, otherwise `Guid.CreateVersion7()`, and records it.
    - `IReadOnlyList<Guid> AllocatedIds`
    - `void Warn(string code, string message)` and `IReadOnlyList<PreviewWarning> Warnings`
    - Internal constructor `(AppDbContext db, TimeProvider time, IReadOnlyList<Guid>? replayIds, CancellationToken ct)`.
  - `sealed record PreviewWarning(string Code, string Message)`
  - `enum ActionKind { Mutation, Query }`
  - `sealed record ActionDescriptor(string Name, string Description, ActionKind Kind, Type HandlerType, Type InputType, Type? AggregateType, Type? OutputType, IReadOnlyList<Type> ErrorTypes, bool HasValidator, bool Destructive, TimeSpan PreviewLifetime)`:
    - `IReadOnlyList<string> ErrorCodes` comes from `ErrorTypes` via `ErrorMetadata`, plus `validation.failed` when `HasValidator`. It is distinct and sorted.
    - Internal compiled invokers return `Task<IOneOf>`: `LoadAsync`, `ApplyAsync`, `RunAsync`, `Summarize`, `ToResult`.
  - `sealed class ActionRegistryOptions` with `List<Assembly> Assemblies` (defaults to the Api assembly) and `List<Type> Types`.
  - `sealed class ActionRegistry`:
    - `IReadOnlyList<ActionDescriptor> All` and `ActionDescriptor? Find(string name)`.
    - `static ActionRegistry Build(IEnumerable<Type> candidates, Func<Type, bool> hasValidator)`.
  - `sealed class ActionRegistrationException(IReadOnlyList<string> violations) : Exception` lists every violation in its message.
  - **Build rules.** Each violation message names the type and the rule:
    - A name must match `^[a-z][a-z0-9_]*$`, and names must be unique.
    - Descriptions must be non-empty.
    - The input must be a record: it has a compiler-generated `PrintMembers`.
    - The convention methods must exist with `Task<OneOf<…>>` returns, and T0 must be `TAggregate` (Load), `Success` (Apply) or `TOutput` (Run).
    - Every non-T0 OneOf argument must derive from `AppError`.
    - A class can't have both `[Action]` and `[Query]`.
    - An `[Action]` must implement `IMutation<,>`, and a `[Query]` must implement `IQuery<,>`.

- [ ] **Step 1: Write the failing tests.** Nested sample types:
  - a valid `SampleAgg : AggregateRoot<SampleId>`;
  - `ValidMutation` (`[Action("sample_do", "Does it.")]`, whose Load returns `OneOf<SampleAgg, SampleNotFound>` and whose Apply returns `OneOf<Success, SampleLocked>`);
  - `ValidQuery`;
  - one invalid type per rule.

```csharp
[Fact] public void Build_ValidMutation_ListsErrorCodesFromLoadAndApply()
{ var r = ActionRegistry.Build([typeof(ValidMutation)], _ => false);
  var d = r.Find("sample_do").ShouldNotBeNull(); d.Kind.ShouldBe(ActionKind.Mutation);
  d.ErrorCodes.ShouldBe(["sample.locked", "sample.not_found"]); d.PreviewLifetime.ShouldBe(TimeSpan.FromSeconds(300)); }
[Fact] public void Build_InputHasValidator_AddsValidationFailedCode()
=> ActionRegistry.Build([typeof(ValidMutation)], _ => true).Find("sample_do")!.ErrorCodes.ShouldContain("validation.failed");
[Fact] public void Build_SeveralInvalidTypes_ReportsEveryViolation()
{ var ex = Should.Throw<ActionRegistrationException>(() => ActionRegistry.Build(
    [typeof(NonErrorArg), typeof(BadName), typeof(EmptyDescription), typeof(ClassInput), typeof(MissingApply), typeof(DuplicateA), typeof(DuplicateB)], _ => false));
  ex.Violations.Count.ShouldBe(7);
  ex.Violations.ShouldContain(v => v.Contains(nameof(NonErrorArg)) && v.Contains("AppError")); /* one ShouldContain per type */ }
[Fact] public async Task NewId_WithReplayIds_ReturnsThemInOrderThenFresh() { /* ctx with replay [a,b]: NewId()==a, ==b, third is new; AllocatedIds == [a,b,third] */ }
[Fact] public void Warn_Twice_RecordsBoth() { }
```

- [ ] **Step 2: Run** `dotnet test tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~ActionRegistryTests"`. Expected: the build fails.
- [ ] **Step 3: Implement.** Build the invokers with `System.Linq.Expressions` and cast the awaited `Task<T>` result to `IOneOf`.
- [ ] **Step 4: Run the same filter.** Expected: PASS.
- [ ] **Step 5: Commit** with `feat(actions): add action contracts, context and validating registry`.

---

### Task 7: FluentValidation bridge

**Files:**
- Create: `src/AppTemplate.Api/Actions/Validation/ValidationRuleExtensions.cs`, `ValidationFailedMapper.cs`
- Test: `tests/AppTemplate.Api.Tests/Actions/ValidationBridgeTests.cs`

**Interfaces:**
- Consumes: `ValidationError`, `ValidationFailed`, `FieldFailure`, `ProjectName`.
- Produces:
  - `static IRuleBuilderOptionsConditions<T, TProp> MustBeValid<T, TProp, TVo>(this IRuleBuilder<T, TProp> rule, Func<TProp, OneOf<TVo, ValidationError>> create)`
    - Runs `create` inside `Custom`. On the error branch it adds a failure with `ErrorCode = error.Code`, `CustomState = new SuggestionState(error.Suggestion)` and message = `error.Message`.
  - `static IRuleBuilderOptions<T, TProp> WithSuggestion<T, TProp>(this IRuleBuilderOptions<T, TProp> rule, string suggestion)`, which sets `CustomState`.
  - `sealed record SuggestionState(string Suggestion)`
  - `static class ValidationFailedMapper` with `static ValidationFailed From(ValidationResult result)`:
    - Fields are camelCase property paths (`Items[0].Name` → `items[0].name`).
    - The code is the explicit `ErrorCode` when it contains a `.`. Otherwise it's derived as `{snake(property)}.{snake(validator name without the "Validator" suffix)}`: `NotEmptyValidator` on `ProjectId` → `project_id.not_empty`.
    - The suggestion is `SuggestionState.Suggestion`, or else `"Provide a valid value for {field}."`

- [ ] **Step 1: Write the failing tests.** A sample validator for `record SampleInput(Guid ProjectId, string Name)`:
  - `RuleFor(x => x.ProjectId).NotEmpty().WithSuggestion("Pass an id from list_projects.")`
  - `RuleFor(x => x.Name).MustBeValid(ProjectName.Create)`

```csharp
[Fact] public async Task From_BothFieldsInvalid_ReturnsAllFailures()
{ var f = ValidationFailedMapper.From(await new SampleValidator().ValidateAsync(new SampleInput(Guid.Empty, "   "), TestContext.Current.CancellationToken));
  f.Fields.Count.ShouldBe(2);
  f.Fields.ShouldContain(x => x.Field == "projectId" && x.Code == "project_id.not_empty" && x.Suggestion == "Pass an id from list_projects.");
  f.Fields.ShouldContain(x => x.Field == "name" && x.Code == "project_name.empty"); }
[Fact] public async Task MustBeValid_ValidName_NoFailures() { /* "Alpha" + new Guid → IsValid */ }
[Fact] public async Task From_RuleWithoutSuggestion_UsesDefaultSuggestion() { }
```

- [ ] **Step 2: Run** `dotnet test tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~ValidationBridgeTests"`. Expected: the build fails.
- [ ] **Step 3: Implement.** Read FluentValidation's `ValidationFailure` and `IValidationContext` members from the installed package before you write code.
- [ ] **Step 4: Run the same filter.** Expected: PASS.
- [ ] **Step 5: Commit** with `feat(validation): bridge value objects and FluentValidation into ValidationFailed`.

---

### Task 8: Change set, hashing and preview token

**Files:**
- Create: `src/AppTemplate.Api/Actions/Preview/*.cs`
- Create (test fixture, reused by later tasks): `tests/AppTemplate.Api.Tests/Fixtures/Notes/{NoteId,NoteText,Note,NoteErrors,NoteEvents,NoteModelContributor}.cs`
- Test: `tests/AppTemplate.Api.Tests/Actions/PreviewTokenSignerTests.cs`, `ChangeSetTests.cs`

**Interfaces:**
- Produces:
  - `sealed record Change(string Entity, string Id, string Op, string Field, JsonNode? Before, JsonNode? After)`
    - `Op` ∈ `added|modified|deleted`, with one row per changed scalar field.
    - Values are provider (primitive) values, found via each property's value converter.
    - Only `Version` is excluded. Shadow FK properties are included, so a child `TaskItem` row has the field `projectId`.
    - `Entity` is the CLR type name; `Id` is the primary-key provider value as a string; `Field` is camelCase.
  - `sealed record SideEffect(string Event, JsonNode Payload)`
  - `static class ChangeSetReader` with `static IReadOnlyList<Change> Read(ChangeTracker tracker)`. Results are ordered by Entity, then Id, then Field.
  - `static class ChangeSetHasher` with `static string Hash(IReadOnlyList<Change> changes, IReadOnlyList<SideEffect> sideEffects, IReadOnlyList<PreviewWarning> warnings)`. It returns hex SHA-256 over canonical JSON.
  - `static class InputHasher` with `static string Hash(object input, Type inputType, JsonSerializerOptions json)`, which re-serializes the deserialized input.
  - `sealed record AggregateVersion(string Type, string Id, int Version)`
  - `sealed record PreviewTokenPayload(string Action, string InputHash, IReadOnlyList<AggregateVersion> AggregateVersions, string ChangesHash, IReadOnlyList<Guid> AllocatedIds, DateTimeOffset ExpiresAt)`
  - `sealed class PreviewOptions` with `string SigningKey`, validated on start to be ≥ 32 UTF-8 bytes. In Development, if it's missing, generate a random key and log a warning.
  - `sealed class PreviewTokenSigner(IOptions<PreviewOptions> options)`:
    - `string Sign(PreviewTokenPayload payload)` produces `base64url(json).base64url(hmac)`.
    - `OneOf<PreviewTokenPayload, PreviewTokenInvalid> Verify(string? token)`. It uses a constant-time compare and returns `PreviewTokenInvalid` on any parse error.
  - Fixture `Note : AggregateRoot<NoteId>` (test assembly, namespace `AppTemplate.Api.Tests.Fixtures.Notes`):
    - `NoteText` is 1–50 characters, with the codes `note_text.empty` and `note_text.too_long`.
    - `bool Locked`.
    - `static Note Create(NoteId, NoteText)` raises `NoteCreated`.
    - `OneOf<Success, NoteLocked> Edit(NoteText)` raises `NoteEdited` unless the text is unchanged.
    - `OneOf<Success, NoteLocked> Lock()`.
    - Errors: `NoteNotFound` (`note.not_found`, `NotFound`) and `NoteLocked` (`note.locked`, `Conflict`).
    - `NoteModelContributor : IModelContributor` maps the `Notes` table with explicit converters. Its `SchemaSql` is the matching `CREATE TABLE`.

- [ ] **Step 1: Write the failing tests.**
  - `PreviewTokenSignerTests`:
    - `Verify_SignedPayload_RoundTrips`
    - `Verify_FlippedPayloadChar_ReturnsInvalid`
    - `Verify_OtherKey_ReturnsInvalid`
    - `Verify_Garbage_ReturnsInvalid`, for `""`, `"abc"`, `"a.b"` and `null`
    - `Options_ShortKey_FailsValidation`, with a 31-byte key
  - `ChangeSetTests` (using `SqliteTestDatabase` + `NoteModelContributor`):

```csharp
[Fact] public async Task Read_ModifiedNote_ReturnsTextBeforeAfterOnly()
{ /* save note "a"; new context; load; Edit("b") */
  var c = ChangeSetReader.Read(ctx.ChangeTracker).ShouldHaveSingleItem();
  (c.Entity, c.Id, c.Op, c.Field).ShouldBe(("Note", id.ToString(), "modified", "text"));
  c.Before!.GetValue<string>().ShouldBe("a"); c.After!.GetValue<string>().ShouldBe("b"); }
[Fact] public void Read_AddedProjectWithTask_ListsAllFieldsIncludingChildProjectId() { }
[Fact] public void Read_VersionIncremented_IsExcluded() { }
[Fact] public void Hash_SameChangesDifferentOrder_IsEqual() { }
[Fact] public void Hash_DifferentWarning_Differs() { }
```

- [ ] **Step 2: Run** `dotnet test tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~PreviewTokenSignerTests|FullyQualifiedName~ChangeSetTests"`. Expected: the build fails.
- [ ] **Step 3: Implement.**
  - Read the `EntityEntry`/`PropertyEntry` API and the value-converter accessors in the installed EF Core 10 before writing code.
  - For complex properties, include `entry.ComplexProperties` the same way, with the field `outer.inner`.
- [ ] **Step 4: Run the same filter.** Expected: PASS.
- [ ] **Step 5: Commit** with `feat(preview): add change-set reader, hashing and HMAC preview token`.

---

### Task 9: Action pipeline and HTTP endpoints

**Files:**
- Create:
  - `src/AppTemplate.Api/Actions/{ActionPipeline,Responses,ActionServiceCollectionExtensions}.cs`
  - `src/AppTemplate.Api/Actions/Events/*.cs` and `src/AppTemplate.Api/Actions/Http/ActionEndpoints.cs`
  - Fixture actions and validators: `tests/AppTemplate.Api.Tests/Fixtures/Notes/{CreateNote,EditNote,LockNote,Explode,GetNote,NoteValidators,RecordingEventHandler,NoteFixture}.cs`
- Modify: `Program.cs`, and `ApiFactory`/`ApiTestBase`, to add the request helpers and fixture registration
- Test: `tests/AppTemplate.Api.Tests/Actions/PipelineTests.cs`

**Interfaces:**
- Consumes: Tasks 5–8.
- Produces:
  - Responses:
    - `sealed record PreviewResponse(string Action, string Summary, IReadOnlyList<Change> Changes, IReadOnlyList<SideEffect> SideEffects, IReadOnlyList<PreviewWarning> Warnings, string PreviewToken, DateTimeOffset ExpiresAt)`
    - `sealed record ExecuteResponse(string Action, object Result, IReadOnlyList<Change> Changes)`
  - `sealed class ActionPipeline`, with dependencies `ActionRegistry`, `IServiceScopeFactory`, `PreviewTokenSigner`, `TimeProvider`, `IOptions<JsonOptions>`, `DomainEventDispatcher`, `ActionMetrics` and `ILogger<ActionPipeline>`:
    - `Task<OneOf<PreviewResponse, AppError>> PreviewAsync(ActionDescriptor action, JsonElement body, CancellationToken ct)`
    - `Task<OneOf<ExecuteResponse, AppError>> ExecuteAsync(ActionDescriptor action, JsonElement body, CancellationToken ct)`. The body holds the input fields plus `previewToken`.
    - `Task<OneOf<object, AppError>> QueryAsync(ActionDescriptor action, JsonElement body, CancellationToken ct)`
    - Each phase runs in its own DI scope with a fresh `AppDbContext`.
    - **Algorithms (from spec §7.4), with these refinements:**
      - Deserialization failures → `RequestMalformed(detail, jsonPath)`.
      - The validator is resolved as `IValidator<TInput>` from the scope.
      - The preview's `aggregateVersions` comes from tracked `IAggregateRoot` entries in state `Unchanged`/`Modified` after `Load`.
      - The execute order is:
        1. Verify the token.
        2. Check the action and input hash.
        3. Check expiry.
        4. Load, replaying `AllocatedIds`.
        5. Already-executed check: any `Added` aggregate entry whose `GetDatabaseValuesAsync()` is non-null → `PreviewAlreadyExecuted`.
        6. Version check → stale `version_changed`.
        7. Apply → a domain error here → stale `domain_error`, with `Cause = {code, message}`.
        8. Hash check → stale `changes_differ`.
        9. **If the change set is empty, skip saving and the version bump.**
        10. Otherwise, increment the root `Version` and save. `DbUpdateConcurrencyException` → stale `concurrent_write`.
        11. Dispatch events after commit.
        12. Call `ToResult`.
      - Domain events are cleared after preview and never dispatched from a preview.
  - Events:
    - `interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent { Task Handle(TEvent domainEvent, CancellationToken ct); }`
    - `sealed class DomainEventDispatcher(IServiceProvider sp)` with `Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken ct)`.
  - `static IServiceCollection AddActions(this IServiceCollection services, Action<ActionRegistryOptions>? configure = null)` registers:
    - the registry singleton, built lazily from the options and an `IValidator<>` probe;
    - the pipeline, signer, dispatcher and `TimeProvider.System` (via TryAdd);
    - the validators from the Api assembly;
    - global JSON options: camelCase and the snake-case-lower enum converter.
  - `static IEndpointRouteBuilder MapActions(this IEndpointRouteBuilder app)` maps one concrete route per descriptor:
    - `POST /api/actions/{name}/preview` → 200 `PreviewResponse`
    - `POST /api/actions/{name}` → 200 `ExecuteResponse`
    - `POST /api/queries/{name}` → 200 output
    - Errors go through `AppErrorProblemDetails.ToResult`.
    - It reads the raw body as `JsonElement`. An empty or invalid body → `request.malformed`.
  - `NoteFixture.Register(IServiceCollection)` adds:
    - types `CreateNote` (`fixture_create_note`), `EditNote` (`fixture_edit_note`, which warns `note.unchanged` when the text is equal), `LockNote` (`fixture_lock_note`, `Destructive = true`), `Explode` (`fixture_explode`, whose Apply throws `InvalidOperationException("boom-secret")`) and `GetNote` (`fixture_get_note`);
    - the validators, `RecordingEventHandler` (a singleton `List<IDomainEvent> Received`) and `NoteModelContributor`.
    - `ApiFactory` calls it by default.
  - `ApiTestBase` helpers. All of them return `ApiResponse`:
    - `Preview(string action, object input)`
    - `Execute(string action, object input, string token)`, which merges `previewToken` into the input JSON object
    - `Task<(ApiResponse preview, ApiResponse execute)> PreviewAndExecute(string action, object input)`
    - `Query(string action, object input)`
    - `string TokenOf(ApiResponse preview)`
    - `string IdFromChanges(ApiResponse preview, string entity = "Note")` returns the `id` of the first `added` change for that entity.
  - `ApiFactory(bool includeFixtures = true)`. Task 11 passes `false`.

- [ ] **Step 1: Write the failing tests** in `PipelineTests : ApiTestBase`. Each test sets up its own notes through the helpers.

```csharp
[Fact] public async Task Preview_CreateNote_ReturnsChangesSummaryTokenAndNoCommit()
{ var p = await Preview("fixture_create_note", new { text = "hello" });
  p.ShouldSucceed(); p.Body.GetProperty("changes").EnumerateArray().ShouldContain(c => c.GetProperty("field").GetString() == "text");
  p.Body.GetProperty("sideEffects")[0].GetProperty("event").GetString().ShouldBe("NoteCreated");
  (await Query("fixture_get_note", new { noteId = IdFromChanges(p) })).ShouldFailWith("note.not_found");
  Factory.Services.GetRequiredService<RecordingEventHandler>().Received.ShouldBeEmpty(); }
[Fact] public async Task Execute_ValidToken_CommitsReturnsResultAndDispatchesEvent() { }
[Fact] public async Task Execute_SameUpdateTokenTwice_SecondIsStale() // Review Focus 1
{ /* create note; preview edit; execute → ok; execute same token again → ShouldFailWith("preview.stale", Conflict) and details.reason == "version_changed" */ }
[Fact] public async Task Execute_SameCreateTokenTwice_SecondIsAlreadyExecuted() // Review Focus 1
{ /* … ShouldFailWith("preview.already_executed", HttpStatusCode.Conflict) */ }
[Fact] public async Task Execute_TamperedToken_Returns400TokenInvalid() { }
[Fact] public async Task Execute_DifferentInput_ReturnsTokenMismatch() { }
[Fact] public async Task Execute_TokenFromOtherAction_ReturnsTokenMismatch() { /* edit token on fixture_lock_note */ }
[Fact] public async Task Execute_After301Seconds_ReturnsExpired() { /* Factory.Time.Advance(TimeSpan.FromSeconds(301)) */ }
[Fact] public async Task Execute_NoteEditedSincePreview_ReturnsStaleVersionChanged() { }
[Fact] public async Task Execute_NoteLockedSincePreview_ReturnsStale() { /* reason domain_error or version_changed — assert code only */ }
[Fact] public async Task PreviewAndExecute_UnchangedText_EmptyChangesWarningAndVersionKept() // Review Focus 5
{ /* warnings[0].code == "note.unchanged"; changes empty; execute ok; a second preview taken before still executes ok */ }
[Fact] public async Task Preview_InvalidText_Returns422WithFieldErrors()
{ (await Preview("fixture_create_note", new { text = "" })).ShouldHaveFieldError("text", "note_text.empty"); }
[Theory] [InlineData("{\"noteId\":\"not-a-guid\",\"text\":\"x\"}")] [InlineData("[]")] [InlineData("")]
public async Task Preview_MalformedBody_Returns400RequestMalformed(string raw) // Review Focus 2
{ /* POST raw to /api/actions/fixture_edit_note/preview → ShouldFailWith("request.malformed", HttpStatusCode.BadRequest) */ }
[Fact] public async Task PreviewAndExecute_HandlerThrows_Returns500WithoutExceptionText()
{ /* fixture_explode: ShouldFailWith("internal.unexpected", InternalServerError); body raw text ShouldNotContain("boom-secret") */ }
[Fact] public async Task Query_ExistingNote_ReturnsDto() { }
```

- [ ] **Step 2: Run** `dotnet test tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~PipelineTests"`. Expected: the build fails.
- [ ] **Step 3: Implement the pipeline, endpoints, dispatcher and fixture.** Wire `AddActions()` and `MapActions()` into `Program`.
- [ ] **Step 4: Run the same filter.** Expected: PASS (16 test cases).
- [ ] **Step 5: Run the full suite** with `dotnet test AppTemplate.slnx`. Expected: PASS and 0 warnings.
- [ ] **Step 6: Commit** with `feat(actions): add preview/execute/query pipeline and HTTP endpoints`.

---

### Task 10: MCP server from the registry

**Files:**
- Create: `src/AppTemplate.Api/Mcp/{McpSetup,McpToolFactory,ToolDescriptions,McpInstructions}.cs`
- Modify: `Program.cs`
- Test: `tests/AppTemplate.Api.Tests/Mcp/McpParityTests.cs`

**Interfaces:**
- Consumes: `ActionRegistry`, `ActionPipeline`, `AppErrorProblemDetails.ToProblemDetails`.
- Produces:
  - `static IServiceCollection AddAppMcp(this IServiceCollection services)`:
    - `AddMcpServer`, with `ServerInfo` `AppTemplate` and `ServerInstructions = McpInstructions.Text`.
    - `.WithHttpTransport()`.
    - A list-tools handler and a call-tools handler that read the registry at request time.
    - Verify the handler-registration API names in the installed `ModelContextProtocol.AspNetCore` before writing code.
  - In `Program`: `app.MapMcp("/mcp")`.
  - `static class McpToolFactory` with `static IReadOnlyList<Tool> Build(ActionRegistry registry, JsonSerializerOptions json)`:
    - For a mutation: `preview_{name}` (`ReadOnlyHint = true`) and `{name}` (`ReadOnlyHint = false`, `DestructiveHint = descriptor.Destructive`).
    - For a query: `{name}` (`ReadOnlyHint = true`).
    - Input schemas come from `JsonSchemaExporter.GetJsonSchemaAsNode(json, inputType)`. Execute tools add a required string property `previewToken`.
  - `static class ToolDescriptions` with `static string For(ActionDescriptor d, string tool)`:
    - The description text, then `"Possible error codes: a, b, c."`
    - For execute tools, append: `"Requires previewToken from preview_{name}. Show the user the preview summary and changes and get their confirmation before calling this tool."`
  - `McpInstructions.Text` explains preview → confirm → execute, the 300 s token lifetime, `preview.stale` → re-preview, and that errors are ProblemDetails with `code`/`suggestion`/`traceId`.
  - Call results:
    - Success → `CallToolResult` with `StructuredContent` = the response JSON and a text block with the same JSON.
    - `AppError` → `IsError = true`, with the ProblemDetails JSON (`instance` = `mcp:{tool}`) as structured content and text.
    - An unknown tool → an error result with `request.not_found` and the suggestion `"Call tools/list to see available tools."`

- [ ] **Step 1: Write the failing tests.** `McpParityTests : ApiTestBase` creates an MCP client against the test server, using `HttpClientTransport` over `Client` at `/mcp`. Verify the client API in the installed `ModelContextProtocol` first.

```csharp
[Fact] public async Task ListTools_Registry_ExactlyOneEntryPerToolName()
{ var registry = Factory.Services.GetRequiredService<ActionRegistry>(); registry.All.Count.ShouldBeGreaterThan(0);
  var expected = registry.All.SelectMany(d => d.Kind == ActionKind.Mutation ? new[] { $"preview_{d.Name}", d.Name } : new[] { d.Name }).OrderBy(x => x).ToList();
  (await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Select(t => t.Name).OrderBy(x => x).ToList().ShouldBe(expected); }
[Fact] public async Task ListTools_ExecuteTool_DescribesErrorsTokenAndConfirmation() { /* fixture_edit_note: contains "note.locked", "validation.failed", "previewToken", "confirmation" */ }
[Fact] public async Task ListTools_Annotations_MatchKindAndDestructive() { /* preview_* ReadOnlyHint true; fixture_lock_note DestructiveHint true */ }
[Fact] public async Task CallTool_PreviewThenExecute_CreatesNote() { /* end-to-end through MCP; then fixture_get_note via MCP returns text */ }
[Fact] public async Task CallTool_StaleToken_ReturnsIsErrorWithProblemJson() { /* IsError true; structured content code == "preview.stale", has suggestion & traceId */ }
[Fact] public async Task Initialize_Server_ReturnsInstructionsMentioningPreview() { }
```

- [ ] **Step 2: Run** `dotnet test tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~McpParityTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the same filter.** Expected: PASS (6 tests).
- [ ] **Step 5: Commit** with `feat(mcp): expose registry actions as MCP tools with previews and ProblemDetails errors`.

---

### Task 11: OpenAPI document and snapshot

**Files:**
- Create: `src/AppTemplate.Api/Actions/Http/OpenApiSetup.cs`, `scripts/update-openapi.sh`, `src/web/openapi.json` (generated)
- Modify: `ActionEndpoints.cs` (metadata), `Program.cs`
- Test: `tests/AppTemplate.Api.Tests/OpenApi/OpenApiSnapshotTests.cs`

**Interfaces:**
- Produces:
  - `AddAppOpenApi()` / `MapOpenApi()` serve `/openapi/v1.json`.
  - Each action endpoint declares:
    - its request body schema (the input type; for execute, the input plus a required `previewToken` added by an operation transformer);
    - a 200 response type;
    - `application/problem+json` responses for each status its `ErrorCodes` can produce, with a schema that has the four extensions;
    - `operationId` = `preview_{name}` / `{name}`;
    - a description built from `ToolDescriptions.For`.
  - The snapshot test compares the live `/openapi/v1.json` (normalized: indented, keys in stable order) with `src/web/openapi.json`.
    - When `UPDATE_OPENAPI=1`, it rewrites the file and passes.
    - When the files differ, it fails with the message `"OpenAPI drift: run scripts/update-openapi.sh"`.
  - `scripts/update-openapi.sh` runs `UPDATE_OPENAPI=1 dotnet test tests/AppTemplate.Api.Tests --filter FullyQualifiedName~OpenApiSnapshotTests`.
  - The snapshot excludes the `fixture_*` operations, because the test builds the document from a factory without the Note fixture (`ApiFactory(includeFixtures: false)`).

- [ ] **Step 1: Write the failing tests.**
  - `OpenApi_Live_MatchesCommittedSnapshot`.
  - `OpenApi_ExecuteOperation_RequiresPreviewToken`: it checks `rename_project`'s request schema, so it **depends on Task 13**. Write it now with `Skip = "Task 13"` and remove the skip in Task 13.
  - `OpenApi_ErrorResponses_UseProblemJson`, using the `preview_*` operations of whatever actions exist.
- [ ] **Step 2: Run** `dotnet test tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~OpenApiSnapshotTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Run `scripts/update-openapi.sh`, then check that `src/web/openapi.json` exists and is not empty.
- [ ] **Step 4: Run the same filter without the env var.** Expected: PASS.
- [ ] **Step 5: Commit** with `feat(api): add OpenAPI document with problem responses and drift snapshot`.

---

### Task 12: Action telemetry

**Files:**
- Create: `src/AppTemplate.Api/Actions/Telemetry/{ActionTelemetry,ActionMetrics,ActionLog}.cs`
- Modify: `ActionPipeline.cs`, `src/AppTemplate.ServiceDefaults/Extensions.cs`
- Test: `tests/AppTemplate.Api.Tests/Telemetry/ActionTelemetryTests.cs`

**Interfaces:**
- Produces:
  - `static class ActionTelemetry` with `const string Name = "AppTemplate.Actions"` and `static readonly ActivitySource Source`.
  - `sealed class ActionMetrics(IMeterFactory factory)`:
    - Counter `app.actions.calls` and histogram `app.actions.duration` (unit `s`).
    - Tags: `action.name`, `action.phase`, `action.outcome`.
    - `void Record(string name, string phase, string outcome, TimeSpan elapsed)`.
  - `static partial class ActionLog` with `[LoggerMessage]` methods:
    - `ActionFailed(ILogger, string actionName, string phase, string errorCode, string traceId)` at Warning.
    - `ActionUnexpected(ILogger, Exception, string actionName, string phase, string traceId)` at Error.
  - The pipeline wraps each phase in the activity `action {name} {phase}` and sets every tag from Global Constraints. `aggregate.*` is set after Load, and `preview.stale_reason` only when stale.
  - ServiceDefaults:
    - `.WithTracing(t => t.AddSource(ActionTelemetry.Name).AddEntityFrameworkCoreInstrumentation())`
    - `.WithMetrics(m => m.AddMeter(ActionTelemetry.Name))`
    - Add the string constant locally, so ServiceDefaults doesn't reference Api.

- [ ] **Step 1: Write the failing tests.**

```csharp
[Fact] public async Task Preview_Success_EmitsSpanWithSuccessOutcome()
{ var p = await Preview("fixture_create_note", new { text = "x" });
  var span = Factory.Activities.ForTrace(p.TraceId).ShouldContain(a => a.DisplayName == "action fixture_create_note preview");
  span.GetTagItem("action.outcome").ShouldBe("success"); span.GetTagItem("aggregate.type").ShouldBe("Note"); }
[Fact] public async Task Execute_Stale_TagsOutcomeAndStaleReason() { /* outcome "preview.stale", preview.stale_reason "version_changed" */ }
[Fact] public async Task EveryFixtureAction_EachPhase_EmitsSpanWithOutcome()
{ var registry = Factory.Services.GetRequiredService<ActionRegistry>(); registry.All.Count.ShouldBeGreaterThan(0); /* loop; per action call preview (+execute) or query with a known-valid input map; assert span exists with outcome tag */ }
```

- [ ] **Step 2: Run** `dotnet test tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~ActionTelemetryTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run the same filter.** Expected: PASS. Then run the full suite: PASS.
- [ ] **Step 5: Commit** with `feat(telemetry): add action spans, metrics and structured logs`.

---

### Task 13: Projects feature actions

**Files:**
- Create: `src/AppTemplate.Api/Features/Projects/*.cs`
- Test: `tests/AppTemplate.Api.Tests/Features/Projects/{CreateProject,RenameProject,AddTask,CompleteTask,ArchiveProject,ListProjects,GetProject}Tests.cs`
- Modify: `src/web/openapi.json` (regenerated), and remove the `Skip` from the Task 11 test

**Interfaces:**
- Consumes: everything above.
- Produces:
  - DTOs:
    - `ProjectDto(Guid Id, string Name, ProjectStatus Status, int Version, IReadOnlyList<TaskItemDto> Tasks)`
    - `TaskItemDto(Guid Id, string Title, bool Done)`
    - `ProjectSummaryDto(Guid Id, string Name, ProjectStatus Status, int TaskCount, int OpenTaskCount)`
  - Actions (class → name → input → OneOf errors):

    | Class | Name | Input | Load errors | Apply errors | Other |
    |---|---|---|---|---|---|
    | `CreateProject` | `create_project` | `CreateProjectInput(string Name)` | `ProjectNameTaken`, `ValidationFailed` | none (`OneOf<Success>`) | |
    | `RenameProject` | `rename_project` | `RenameProjectInput(Guid ProjectId, string Name)` | `ProjectNotFound` | `ValidationFailed`, `ProjectIsArchived`, `ProjectNameTaken` | |
    | `AddTask` | `add_task` | `AddTaskInput(Guid ProjectId, string Title)` | `ProjectNotFound` | `ValidationFailed`, `ProjectIsArchived` | |
    | `CompleteTask` | `complete_task` | `CompleteTaskInput(Guid ProjectId, Guid TaskId)` | `ProjectNotFound` | `ProjectIsArchived`, `TaskNotFound`, `TaskAlreadyDone` | |
    | `ArchiveProject` | `archive_project` | `ArchiveProjectInput(Guid ProjectId)` | `ProjectNotFound` | `ProjectIsArchived` | `Destructive = true` |
    | `ListProjects` | `list_projects` | `ListProjectsInput(ProjectStatus? Status)` | | | Query; Run → `OneOf<IReadOnlyList<ProjectSummaryDto>>`, ordered by Name |
    | `GetProject` | `get_project` | `GetProjectInput(Guid ProjectId)` | | | Query; Run → `OneOf<ProjectDto, ProjectNotFound>` |

    - `CreateProject.Load` uses `ctx.NewId()` and checks the name holder.
    - The name holder is found with `ctx.Db.Projects.Where(p => p.Status == ProjectStatus.Active && p.Name == name).Select(p => (ProjectId?)p.Id).FirstOrDefaultAsync()`.
    - Every mutation's `ToResult` returns a `ProjectDto`, and its `Summarize` returns a one-line English sentence quoting the names.
  - `ProjectValidators.cs`: one validator per input that has fields.
    - `MustBeValid(ProjectName.Create)` / `MustBeValid(TaskTitle.Create)`.
    - `NotEmpty()` on ids, with `.WithSuggestion("Use an id returned by list_projects or get_project.")`.

- [ ] **Step 1: Write the failing tests.** Every test carries `[Covers(...)]`. Per class:
  - `CreateProjectTests`:
    - `[Covers("create_project", Case.HappyPath)] PreviewAndExecute_NewName_CreatesActiveProject`
    - `[Covers("create_project", "project.name_taken")] Preview_NameDiffersOnlyByCaseAndSpaces_ReturnsNameTaken` (Review Focus 4): create `"Alpha"`, then preview `"  alpha "`.
    - `[Covers("create_project", "validation.failed")] Preview_101CharName_ReturnsTooLong` uses `ShouldHaveFieldError("name","project_name.too_long")`.
    - `[Covers("create_project", Case.Stale)] Execute_SameNameCreatedSincePreview_ReturnsStale`
  - `RenameProjectTests`:
    - happy path
    - `project.not_found`
    - `project.archived`
    - `project.name_taken`
    - `validation.failed`
    - stale: a task is added between preview and execute, so the reason is `version_changed`
    - `PreviewAndExecute_SameName_NoChangesVersionUnchanged` (Review Focus 5)
  - `AddTaskTests`: happy path, where the result has 1 task; `project.not_found`; `project.archived`; `validation.failed`; stale.
  - `CompleteTaskTests`: happy path; `project.not_found`; `project.archived`; `task.not_found`; `task.already_done`; stale.
  - `ArchiveProjectTests`: happy path, after which the archived project's name is reusable by `create_project`; `project.not_found`; `project.archived`; stale.
  - `ListProjectsTests`: happy path with a status filter, where `status: "archived"` returns only archived projects.
  - `GetProjectTests`: happy path; `project.not_found`.
- [ ] **Step 2: Run** `dotnet test tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~Features.Projects"`. Expected: FAIL.
- [ ] **Step 3: Implement the actions, DTOs and validators.**
- [ ] **Step 4: Run the same filter.** Expected: PASS.
- [ ] **Step 5: Regenerate the OpenAPI snapshot.** Run `scripts/update-openapi.sh`, then remove the `Skip` from `OpenApi_ExecuteOperation_RequiresPreviewToken`. Run `dotnet test AppTemplate.slnx`. Expected: PASS, 0 warnings.
- [ ] **Step 6: Commit** with `feat(projects): add project and task actions with integration tests`.

---

### Task 14: Architecture and coverage tests

**Files:**
- Create: `tests/AppTemplate.Architecture.Tests/{ArchitectureTests,CoverageTests,ErrorCodeTests}.cs`
- Test: those files

**Interfaces:**
- Consumes:
  - `ActionRegistry.Build`, `ActionRegistryOptions` (default assemblies), `ErrorMetadata`, `CoversAttribute`/`Case`.
  - The validator probe: `typeof(AbstractValidator<>)` subclasses in the Api assembly, keyed by input type.

- [ ] **Step 1: Write the tests.** Each test that loops asserts its collection is non-empty first.
  - `ArchitectureTests`:
    - `Domain_References_OnlyOneOf`: the referenced assembly names are a subset of `System.*`, `Microsoft.CSharp`, `netstandard` and `OneOf`.
    - `DomainEntities_HaveNoPublicSetters`: covers `Entity<>` subclasses in Domain.
    - `ValueObjects_AreSealedWithoutPublicParameterizedConstructors`: covers `IValueObject<,>` implementers.
    - `Api_DoesNotUseBannedTypes`: NetArchTest `ShouldNot().HaveDependencyOnAny("FluentValidation.ValidationException", "MediatR", "Mediator", "Microsoft.EntityFrameworkCore.InMemory")`. It also checks that no type named `Result` or `Result`1` exists in Domain or Api.
    - `Registry_ApiAssembly_BuildsWithoutViolations`.
  - `CoverageTests`:
    - `EveryAction_HasMatchingTestsClass`: for each Api descriptor, a type named `{HandlerType.Name}Tests` exists in the Api.Tests assembly.
    - `EveryAction_HasCoversForHappyPathEachErrorAndStale`: computes the missing set per action. Mutations require HappyPath, Stale and each error code; queries require HappyPath and each error code. On failure, print every missing `(action, case/code)` pair.
  - `ErrorCodeTests`:
    - `ErrorCodes_AreUniqueAndWellFormed`: covers every concrete `AppError` subclass in Domain and Api.
- [ ] **Step 2: Run** `dotnet test tests/AppTemplate.Architecture.Tests`. Expected: PASS.
- [ ] **Step 3: Prove the checks bite.**
  - Temporarily delete one `[Covers("rename_project", "project.archived")]`. Run again. Expected: FAIL, naming `rename_project project.archived`.
  - Restore it, then run once more. Expected: PASS.
- [ ] **Step 4: Commit** with `test(arch): enforce layering, banned types, error codes and per-action coverage`.

---

### Task 15: Frontend scaffold, generated client and API static hosting

**Files:**
- Create: `src/web/**` (Vite react-ts), `src/web/src/api/{client.ts,actions.ts,schema.d.ts}`, `src/web/components.json`
- Modify: `src/AppTemplate.Api/Program.cs` (static files + SPA fallback), `src/AppTemplate.Api/AppTemplate.Api.csproj` (a publish target)
- Test: `tests/AppTemplate.Api.Tests/Http/SpaFallbackTests.cs`

**Interfaces:**
- Produces:
  - `package.json` scripts:
    - `dev`
    - `build`: `tsc -b && vite build`
    - `typecheck`: `tsc -b --noEmit`
    - `lint`: `eslint .`
    - `gen:api`: `openapi-typescript openapi.json -o src/api/schema.d.ts`
  - Vite configuration:
    - `@tailwindcss/vite`, and the alias `@` → `src`.
    - `build.outDir = "../AppTemplate.Api/wwwroot"` with `emptyOutDir: true`.
    - A dev proxy for `/api` and `/mcp`, targeting `process.env.services__api__https__0 ?? process.env.services__api__http__0 ?? "http://localhost:5180"`.
  - `src/api/client.ts`: `export const api = createClient<paths>({ baseUrl: import.meta.env.VITE_API_BASE_URL ?? "" })`.
  - `src/api/actions.ts`:
    - `type Problem = { type: string; title: string; status: number; detail: string; code: string; details?: unknown; suggestion: string; traceId: string }`
    - `function isProblem(x: unknown): x is Problem`
    - `previewAction(name, input)`, `executeAction(name, input, previewToken)` and `runQuery(name, input)`, typed over the generated `paths` for `/api/actions/{name}/preview`, `/api/actions/{name}` and `/api/queries/{name}`.
  - shadcn: `npx shadcn@latest init`, then `npx shadcn@latest add button dialog table input label badge alert card sonner`.
  - Packages: `@tanstack/react-query @tanstack/react-table react-router openapi-fetch`, and dev `openapi-typescript`.
  - Api:
    - `UseDefaultFiles()` and `UseStaticFiles()`.
    - `MapFallbackToFile("index.html")`, registered **after** the `/api` and `/mcp` fallbacks from Task 5.
    - csproj target `BuildWeb` with `BeforeTargets="ComputeFilesToPublish"` runs `npm ci && npm run build` in `../web`.

- [ ] **Step 1: Write the failing test.** `SpaFallbackTests` uses a factory whose `WebRootPath` is a temp dir containing `index.html` with the body `SPA`:
  - `Get_ClientRoute_ServesIndexHtml`: `/projects/123` → 200 containing `SPA`.
  - `Get_UnknownApiRouteWithSpa_StillProblem404` (Review Focus 3): `/api/nope` → `ShouldFailWith("request.not_found")`.
  - `Get_McpSubRouteWithSpa_StillProblem404`.
- [ ] **Step 2: Run** `dotnet test tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~SpaFallbackTests"`. Expected: FAIL.
- [ ] **Step 3: Scaffold the web app.** Run `npm create vite@latest web -- --template react-ts` in `src/`, then install the packages, Tailwind v4 and shadcn (consult the shadcn docs via Context7). Run `npm run gen:api`.
- [ ] **Step 4: Implement static hosting in `Program`.**
- [ ] **Step 5: Verify.**
  - The tests from Step 1: PASS.
  - `cd src/web && npm run typecheck && npm run lint && npm run build`: exit 0, and `src/AppTemplate.Api/wwwroot/index.html` exists.
- [ ] **Step 6: Commit** with `feat(web): scaffold Vite React app with shadcn, generated API client and SPA hosting`.

---

### Task 16: Shared UI components and Projects pages

**Files:**
- Create:
  - `src/web/src/components/data-table.tsx`, `src/web/src/components/preview-confirm-dialog.tsx`, `src/web/src/components/problem-alert.tsx`
  - `src/web/src/pages/projects-page.tsx`, `src/web/src/pages/project-detail-page.tsx`
  - `src/web/src/main.tsx`, routes and the QueryClient provider
- Test: build checks only. The spec says there are no frontend unit tests in v1.

**Interfaces:**
- Consumes: `previewAction`, `executeAction`, `runQuery`, `isProblem`, `Problem`.
- Produces:
  - `DataTable<TData>({ columns: ColumnDef<TData>[], data: TData[], emptyText?: string })`, built on TanStack Table and the shadcn `table`, with column sorting.
  - `PreviewConfirmDialog<TInput>({ action: string; input: TInput; trigger: ReactNode; title: string; onExecuted?: (result: unknown) => void })`:
    1. On open, it calls `previewAction`.
    2. It renders the `summary`, a changes table (entity, field, before → after), the side effects and the warnings.
    3. Confirm calls `executeAction` with the token, then invalidates the `["projects"]` queries.
    4. A `Problem` renders through `ProblemAlert` with the code, detail and suggestion. On `preview.stale` it shows a "Re-preview" button that re-runs step 1.
  - `ProblemAlert({ problem: Problem })` shows the title, detail, suggestion and a small `traceId`.
  - Routes:
    - `/` → `ProjectsPage`: `DataTable` of `list_projects`, a status filter, and "New project", which opens an input and then `PreviewConfirmDialog` for `create_project`.
    - `/projects/:id` → `ProjectDetailPage`: rename, archive (destructive styling), a tasks `DataTable` with complete, and add task. Every one goes through `PreviewConfirmDialog`.

- [ ] **Step 1: Implement the components and pages.** Add shadcn components only with `npx shadcn@latest add`, and never edit `src/components/ui/*`.
- [ ] **Step 2: Verify** with `cd src/web && npm run typecheck && npm run lint && npm run build`. Expected: exit 0.
- [ ] **Step 3: Check the UI by hand.**
  - Run `dotnet run --project src/AppTemplate.Api --launch-profile http` and `cd src/web && npm run dev`.
  - Open the Vite URL.
  - Create, rename, add a task, complete it and archive. Each step shows a preview first.
  - Report what you observed. If no browser is available, say so, and instead exercise the same flow with `curl` against `/api/actions/*`.
- [ ] **Step 4: Commit** with `feat(web): add DataTable, PreviewConfirmDialog and Projects pages`.

---

### Task 17: Aspire AppHost wiring

**Files:**
- Modify: `src/AppTemplate.AppHost/AppHost.cs` (or the template's `Program.cs`), `src/AppTemplate.AppHost/AppTemplate.AppHost.csproj`, `src/AppTemplate.ServiceDefaults/Extensions.cs`

**Interfaces:**
- Produces:
  - `var db = builder.AddSqlite("appdb")`.
  - `var key = builder.AddParameter("preview-signing-key", secret: true)`, with a generated persistent default. Verify `GenerateParameterDefault` or its Aspire 17 equivalent before using it.
  - `var api = builder.AddProject<Projects.AppTemplate_Api>("api").WithReference(db).WaitFor(db).WithEnvironment("Preview__SigningKey", key)`.
  - `builder.AddViteApp("web", "../web").WithReference(api).WaitFor(api)`, with an npm install step per the Aspire 17 JavaScript hosting API.

- [ ] **Step 1: Install the Aspire CLI if it's missing.** Run `aspire --version`. If that fails, run `curl -sSL https://aspire.dev/install.sh | bash`, which installs at user level. Expected: `17.x`.
- [ ] **Step 2: Implement the AppHost.**
- [ ] **Step 3: Verify the build** with `dotnet build AppTemplate.slnx`. Expected: 0 warnings.
- [ ] **Step 4: Verify it runs.**
  - Run `aspire start` in the background, or `dotnet run --project src/AppTemplate.AppHost`.
  - Wait until `curl -sf http://localhost:5180/health` returns 200.
  - `curl -s -X POST http://localhost:5180/api/actions/create_project/preview -H 'content-type: application/json' -d '{"name":"Smoke"}'` returns JSON with `previewToken`.
  - The dashboard URL is printed, and the resources `appdb`, `api` and `web` are running.
  - Stop the AppHost afterwards.
- [ ] **Step 5: Commit** with `feat(apphost): orchestrate SQLite, Api and Vite with Aspire`.

---

### Task 18: verify.sh and rename.sh

**Files:**
- Create: `scripts/verify.sh`, `scripts/rename.sh` (both `chmod +x`)

**Interfaces:**
- Produces:
  - `scripts/verify.sh` (`set -euo pipefail`; it can run from any cwd because it `cd`s to the repo root) runs, in order:
    1. `dotnet build AppTemplate.slnx`
    2. `dotnet test AppTemplate.slnx --no-build`
    3. In `src/web`: install with `npm ci` when `node_modules` is missing.
    4. Generate the schema into a temp file and `diff` it against `src/api/schema.d.ts`. On any difference, print `"Client drift: run npm run gen:api in src/web"` and exit 1.
    5. `npm run typecheck && npm run lint && npm run build`
    6. Finally print `VERIFY OK`.
  - `scripts/rename.sh <NewName>`:
    - Validates `^[A-Z][A-Za-z0-9]+$`.
    - Over `git ls-files`, replaces `AppTemplate` → `NewName` and `apptemplate` → lowercased name in file contents.
    - Renames paths containing `AppTemplate`.
    - Prints the next steps, including `dotnet build`.

- [ ] **Step 1: Write `verify.sh`.** Run `scripts/verify.sh; echo "exit=$?"`. Expected: `VERIFY OK` and `exit=0`.
- [ ] **Step 2: Prove the failure path.**
  - Temporarily add `[Fact] public void Broken() => 1.ShouldBe(2);` to any test class.
  - Run `scripts/verify.sh; echo "exit=$?"`. Expected: a nonzero exit, and `VERIFY OK` is not printed.
  - Remove the test.
  - Then edit one character in `src/web/src/api/schema.d.ts`. Expected: a nonzero exit with "Client drift".
  - Revert the edit.
- [ ] **Step 3: Write `rename.sh`.**
  - Test it in a scratch copy: `git clone . "$SCRATCH/rename-check" && cd "$SCRATCH/rename-check" && scripts/rename.sh Acme && dotnet build Acme.slnx`.
  - Expected: build succeeded, and `grep -ri apptemplate --exclude-dir=.git . || true` prints nothing.
- [ ] **Step 4: Commit** with `chore(scripts): add verify and rename scripts`.

---

### Task 19: Agent instructions, plugins, rules and MCP config

**Files:**
- Create: `AGENTS.md`, `CLAUDE.md`, `.claude/settings.json`, `.mcp.json`, `.claude/rules/*.md`

**Interfaces:**
- Produces:
  - `CLAUDE.md`: exactly `@AGENTS.md`.
  - `AGENTS.md` sections, in order. Keep it under about 250 lines, and link to skills for procedures.
    1. What this is (both audiences)
    2. Architecture map (§7.1 tree)
    3. Golden rules:
       - define actions once via `[Action]`/`[Query]`;
       - OneOf only;
       - value objects own rules;
       - every action gets a `{Class}Tests` with `[Covers]`;
       - never hand-write endpoints or MCP tools;
       - no provider-specific SQL in features.
    4. Preview protocol
    5. Errors: ProblemDetails, the code format and good suggestions
    6. Commands: `scripts/verify.sh`, running via Aspire, `dotnet tool run dotnet-ef migrations add <Name> -p src/AppTemplate.Api -o Data/Migrations`, `scripts/update-openapi.sh`, `npm run gen:api`
    7. Observability and debugging (see `skill:investigate`)
    8. Frontend rules (spec §7.11)
    9. Docs-lookup rule (spec §7.11)
    10. dotnet-claude-kit overrides: the opening sentence and table verbatim from spec §7.11.1, plus the "kit agrees" list
    11. Plugins and prerequisites: Aspire CLI, `dotnet tool install -g CWM.RoslynNavigator`, and an optional Context7 key via `CONTEXT7_API_KEY`
    12. "Never run" list: `aspire agent init`, `/dotnet-init`
    13. Swap to Postgres: a pointer to `skill:swap-to-postgres`
  - `.claude/settings.json`:
    - `extraKnownMarketplaces` for `codewithmukesh/dotnet-claude-kit`, `microsoft/aspire-skills`, `dotnet/skills` and `microsoftdocs/mcp`.
    - `enabledPlugins` for `dotnet-claude-kit`, `aspire`, `dotnet`, `dotnet-data` and `microsoft-docs`.
    - Verify the key names and the marketplace/plugin ids against the current Claude Code settings docs and each repo's `marketplace.json`.
  - `.mcp.json` `mcpServers`:
    - `aspire` (`aspire agent mcp`)
    - `shadcn` (`npx shadcn@latest mcp`)
    - `context7` (http `https://mcp.context7.com/mcp`, with the header from `${CONTEXT7_API_KEY}` if supported)
    - `app` (http `http://localhost:5180/mcp`)
    - Add the Microsoft Learn MCP server only if the `microsoft-docs` plugin doesn't bundle it.
  - `.claude/rules/`: all 10 rule files from `codewithmukesh/dotnet-claude-kit` at tag `v0.14.0` (or the latest tag), fetched raw. Read each one before committing.
    - Edit `error-handling.md`, `architecture.md` and `testing.md` to match the overrides table.
    - Each edited file starts with `> Edited for this template: differs from dotnet-claude-kit on <topics>. See AGENTS.md → dotnet-claude-kit overrides.`

- [ ] **Step 1: Fetch the rules** into a new scratch dir, read them, then copy them into `.claude/rules/` and make the edits.
- [ ] **Step 2: Write `AGENTS.md`, `CLAUDE.md`, `.claude/settings.json` and `.mcp.json`.**
- [ ] **Step 3: Verify.**
  - `python3 -I -m json.tool .claude/settings.json >/dev/null && python3 -I -m json.tool .mcp.json >/dev/null`: exit 0.
  - `grep -c "this file wins" AGENTS.md` prints ≥ 1.
  - `ls .claude/rules | wc -l` prints 10.
- [ ] **Step 4: Commit** with `docs(agents): add AGENTS.md, kit overrides, plugin settings, rules and MCP config`.

---

### Task 20: Repo-owned skills, shadcn skill and skill lint

**Files:**
- Create: `.claude/skills/{domain-modeling,ef-core,tdd,add-action,add-aggregate,change-action-contract,frontend-feature,investigate,remove-example,rename-template,swap-to-postgres}/SKILL.md`, the vendored `.claude/skills/shadcn/`, and `tests/AppTemplate.Architecture.Tests/SkillLintTests.cs`

**Interfaces:**
- Produces:
  - SKILL.md conventions:
    - YAML frontmatter with `name` (equal to the folder) and a `description` that starts with "Use when".
    - References to other repo skills are written `` `skill:<name>` ``.
    - Repo paths in backticks start with `src/`, `tests/`, `scripts/`, `docs/` or `.claude/`.
    - Commands in backticks start with `dotnet`, `npm run` or `scripts/`.
  - Content per skill: the purpose rows of spec §7.11.1. Each skill also has:
    - delta skills open with ``Base: `dotnet-claude-kit:<skill>`. Differences in this template:``;
    - `add-action` walks the exact order from the spec, with file paths taken from `src/AppTemplate.Api/Features/Projects/RenameProject.cs` as the worked example;
    - `remove-example` lists every Projects file:
      - Domain: `Projects/`;
      - Api: `Features/Projects`, `ProjectConfiguration`, `TaskItemConfiguration`, the `Projects` DbSet;
      - tests: `Features/Projects`, `Domain/Project*`, `Domain/TaskTitleTests`;
      - web: the pages and routes.

      Plus: delete `Data/Migrations/*` and re-add `InitialCreate`, then run `scripts/update-openapi.sh`, `npm run gen:api` and `scripts/verify.sh`;
    - `swap-to-postgres` covers the NOCASE collation replacement (a `citext` or a normalized column) and the Testcontainers decision for tests.
  - The shadcn skill comes from `npx -y skills add shadcn/ui --skill shadcn --agent claude-code`. Read ui.shadcn.com/docs/skills first; if the command differs, follow the docs.
  - `SkillLintTests`. Find the repo root by walking up to `AppTemplate.slnx`.
    - `RepoSkills_AllExpectedPresent`: the 11 names exist.
    - `RepoSkills_HaveValidFrontmatter`
    - `RepoSkills_ReferencedPathsExist`
    - `RepoSkills_ReferencedNpmScriptsExist`: checked against `src/web/package.json`.
    - `RepoSkills_ReferencedSkillsExist`
    - The vendored `shadcn` folder is excluded from all of these.

- [ ] **Step 1: Write `SkillLintTests`.** Run `dotnet test tests/AppTemplate.Architecture.Tests --filter "FullyQualifiedName~SkillLintTests"`. Expected: FAIL (the skills are missing).
- [ ] **Step 2: Write the 11 skills. Install the shadcn skill.**
- [ ] **Step 3: Run the same filter.** Expected: PASS.
- [ ] **Step 4: Prove the lint bites.** Temporarily change one path in `add-action/SKILL.md` to `src/Nope.cs`. Expected: FAIL naming it. Revert.
- [ ] **Step 5: Commit** with `docs(skills): add repo-owned skills, vendored shadcn skill and skill lint`.

---

### Task 21: Definition-of-done verification (spec §9)

**Files:** none new, unless a check fails. A fix goes in its own commit.

- [ ] **Step 1: Run verify on a fresh clone.** `git clone . "$SCRATCH/fresh" && cd "$SCRATCH/fresh" && scripts/verify.sh`. Expected: `VERIFY OK`.
- [ ] **Step 2: Check the AppHost without Docker.**
  - `dotnet run --project src/AppTemplate.AppHost` starts the dashboard, `appdb`, `api` and `web`.
  - Confirm `GET /health` returns 200.
- [ ] **Step 3: Run the MCP flow against the running app** with a small MCP client: the `ModelContextProtocol` client in a `dotnet run` scratch console, or `npx @modelcontextprotocol/inspector --cli`.
  - `preview_create_project` → `create_project` with the token → `list_projects` contains the project.
  - Then execute a reused stale token. Expected: `isError` with `code` `preview.stale`, a `suggestion` and a `traceId`.
  - Find that `traceId` through the Aspire MCP or the dashboard traces, to prove the debug path.
- [ ] **Step 4: Check the UI.** Through the web resource, list, create, rename and archive a project via the preview dialog. Report what you observed, or say that no browser was available.
- [ ] **Step 5: Dry-run `remove-example`.** In a scratch clone, follow `.claude/skills/remove-example/SKILL.md` literally, then run `scripts/verify.sh`. Expected: `VERIFY OK`. Fix the skill (not the scratch copy) if any step was wrong, and re-run.
- [ ] **Step 6: Report.** Give each check's command and its observed result. Commit any fixes, each separately.
