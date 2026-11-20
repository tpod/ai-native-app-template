# AGENTS.md

## What this is

A full-stack .NET 10 + React app template built for two kinds of agent:

- **Coding agents** (you, reading this) build features. Read this file first; it wins over any plugin or kit advice.
- **Runtime agents** use the running app through its MCP server. Every action you add is an MCP tool for them.

One `[Action]` or `[Query]` class defines a feature once. The action registry turns it into HTTP endpoints, MCP tools, OpenAPI operations and a preview → confirm → execute flow. Procedures live in skills, linked as `skill:<name>`.

## Architecture map

```
AppTemplate.slnx
Directory.Build.props / Directory.Packages.props   # net10.0, warnings as errors, central package versions
AGENTS.md  CLAUDE.md  .mcp.json  .claude/{settings.json,rules/,skills/}
scripts/verify.sh  scripts/update-openapi.sh  scripts/rename.sh
src/
  AppTemplate.AppHost/          # Aspire: SQLite appdb, api, Vite web
  AppTemplate.ServiceDefaults/  # OpenTelemetry, health checks, resilience
  AppTemplate.Domain/           # aggregates, entities, value objects, errors, events. References only OneOf
    Common/                     # AggregateRoot, AppError, ErrorAttribute, IValueObject, IDomainEvent, ValidationFailed
    Projects/                   # the example aggregate (remove with skill:remove-example)
  AppTemplate.Api/
    Actions/                    # framework: registry, pipeline, preview token, validation bridge, Http/, Telemetry/
    Features/<Aggregate>/       # one file per action or query, plus <Aggregate>Validators.cs
    Data/                       # AppDbContext, EF configurations, ValueObjectConventions, Migrations/
    Mcp/                        # MCP wiring and server instructions; tools are generated from the registry
  web/                          # React + Vite + TypeScript, Tailwind v4, shadcn/ui, TanStack Query + Table
tests/
  AppTemplate.Api.Tests/        # WebApplicationFactory integration tests (HTTP + MCP)
    Infrastructure/             # ApiFactory, ApiTestBase, ApiResponseShouldly, CoversAttribute
    Fixtures/Notes/             # fixture_* actions for framework tests
    Features/<Aggregate>/       # one {ActionClass}Tests per action
  AppTemplate.Architecture.Tests/  # layering, banned types, error codes, [Covers] coverage
```

## Golden rules

1. **Define each action once** with `[Action("snake_name", "description")]` + `IMutation<TInput, TAggregate>`, or `[Query(...)]` + `IQuery<TInput, TOutput>`, in `src/AppTemplate.Api/Features/<Aggregate>/`. The OneOf error arguments of `Load`/`Apply`/`Run` are the declared error codes. Recipe: `skill:add-action`.
2. **OneOf only.** Domain methods and actions return `OneOf<…>`. Never introduce a C# `Result`/`Result<T>` type.
3. **Value objects own their rules.** Private constructor, `static Create(...)` returning `OneOf<TVo, ValidationError>`. Single-value VOs implement `IValueObject<TSelf, TValue>` and are mapped by convention (`ValueObjectConventions`). See `skill:domain-modeling`.
4. **Every action gets a `{ActionClass}Tests` class with `[Covers]`** for the happy path, each error code and, for mutations, the stale case. See `skill:tdd`.
5. **Never hand-write endpoints or MCP tools** for actions. No controllers, `MapPost`, `IEndpointGroup` or `[McpServerTool]`.
6. **No provider-specific SQL in domain or feature code.** Provider details stay in `src/AppTemplate.Api/Data`. See `skill:ef-core`.
7. **Validation never throws.** FluentValidation `AbstractValidator<TInput>` in the Api, using `RuleFor(x => x.Name).MustBeValid(ProjectName.Create)`, `.WithSuggestion("…")` and `.WithErrorCode("…")`. Never `ValidateAndThrow` or `ValidationException`.
8. **Behavior lives on the aggregate.** No public setters. Actions load data, including DB-dependent checks such as uniqueness, and pass it into the domain method.
9. **Changing an action's input, output or error codes changes an MCP contract.** Follow `skill:change-action-contract`.

`tests/AppTemplate.Architecture.Tests` enforces these mechanically. It bans `ValidationException`, Mediator, MediatR, any `Result`/`Result<T>` type (including `OneOf.Types.Result`) and the EF InMemory provider. It also requires `IAggregateRoot.Version` to be a concurrency token, and checks layering, error codes and `[Covers]` coverage.

**Test rules** (details in `.claude/rules/testing.md`):

- Mark coverage with `[Covers("rename_project", "project.name_taken")]` or `[Covers("rename_project", Case.HappyPath)]` / `Case.Stale`. `Case` is globally aliased in `tests/AppTemplate.Api.Tests/GlobalUsings.cs` (Shouldly has its own `Case`).
- `CoverageTests` fails unless every action has HappyPath, every declared error code (including `validation.failed` when a validator exists) and, for mutations, Stale.
- Shouldly only, names `Method_State_Expected`, and pass `TestContext.Current.CancellationToken` where the analyzer asks.
- Framework tests (`Actions/`, `Mcp/`, `OpenApi/`, `Telemetry/`, `Http/`, `Infrastructure/`) use only the `fixture_*` Note actions in `tests/AppTemplate.Api.Tests/Fixtures/Notes` and never reference the Projects example. Register fixtures through `ActionRegistryOptions.Types` (`NoteFixture.Register`), never by scanning the Api.Tests assembly: it holds deliberately invalid handlers.

## Preview protocol

Every mutation is previewed first. Over MCP: `preview_<name>` → show the user `summary` and `changes` (and `warnings`) → get confirmation → `<name>` with the same input plus `previewToken`. Over HTTP: `POST /api/actions/<name>/preview`, then `POST /api/actions/<name>` with the input fields plus `previewToken`. Queries are `POST /api/queries/<name>` and need no preview.

- A preview saves nothing. The token binds the input, the loaded aggregate versions and the change set. It lasts 300 s by default (`PreviewLifetimeSeconds` on `[Action]`) and works once.
- `preview.stale` or `preview.expired` → call the preview again and show the new result. `details.reason` on stale is `version_changed`, `domain_error`, `changes_differ` or `concurrent_write`.
- `preview.already_executed` (a create whose id already exists) → don't retry; query to confirm.
- A no-op mutation previews empty `changes` and does not bump `Version`.
- The UI follows the same flow through `<PreviewConfirmDialog>`.

## Errors

Every error is RFC 9457 ProblemDetails (`application/problem+json`), the same JSON on HTTP and on MCP (`isError: true`): `type`, `title`, `status`, `detail`, `instance`, plus `code`, `details`, `suggestion` and `traceId`. Every response carries an `X-Trace-Id` header.

- Define errors as `sealed record`s deriving from `AppError` with `[Error("area.snake_case", ErrorKind.X, "Stable title")]`. See `src/AppTemplate.Domain/Projects/ProjectErrors.cs`.
- Codes match `^[a-z_]+\.[a-z_]+$` and are unique. Status comes from `ErrorKind`: Validation 422, NotFound 404, Conflict 409, Precondition 409, BadRequest 400, Unexpected 500.
- `validation.failed` lists `details.fields[]` with `{ field, code, message, suggestion }`. Field codes come from the VO error (`project_name.too_long`) or `.WithErrorCode`.
- A good `suggestion` is the next step an agent can take, and names a tool when one helps: "Choose a different name; list_projects shows existing names." It never just restates the error.
- Framework codes: `request.malformed`, `request.not_found`, `internal.unexpected`, `preview.token_invalid`, `preview.token_mismatch`, `preview.expired`, `preview.stale`, `preview.already_executed`.

## Commands

| Task | Command |
|---|---|
| Is it green? Run before claiming done | `scripts/verify.sh` (must print `VERIFY OK`) |
| Run everything (dashboard, SQLite `appdb`, `api`, Vite `web`) | `dotnet run --project src/AppTemplate.AppHost` |
| Full test suite | `dotnet test --solution AppTemplate.slnx` |
| Focused tests | `dotnet test --project tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~RenameProjectTests"` |
| Add a migration (run `dotnet tool restore` once first) | `dotnet tool run dotnet-ef migrations add <Name> -p src/AppTemplate.Api -o Data/Migrations` |
| After changing an action's contract | `scripts/update-openapi.sh`, then `cd src/web && npm run gen:api` |
| Rename the template | `scripts/rename.sh <NewName>` (see `skill:rename-template`) |

- `scripts/verify.sh` runs `dotnet build` (warnings are errors), `dotnet test`, the OpenAPI → TypeScript client drift check, then `npm run typecheck`, `npm run lint` and `npm run build` in `src/web`.
- Tests run on Microsoft.Testing.Platform with xUnit v3. Exit code 8 means zero tests matched: treat it as a failure, not a pass. Check `$?`.
- `dotnet run` uses the AppHost's first launch profile, `http`, which sets `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`. The dashboard is on `http://localhost:15165`; the console prints its login link. The Api is on `http://localhost:5180`, with MCP at `http://localhost:5180/mcp`, OpenAPI at `/openapi/v1.json` and health at `/health`.
- If NuGet restore hangs, set `DOTNET_SYSTEM_NET_DISABLEIPV6=1`. The repo scripts already default it.

## Observability and debugging

Debug in this order (`skill:investigate`): error `code` → `traceId` → trace and logs in the Aspire dashboard or the `aspire` MCP server → the failing test's output.

- Every action phase is a span named `action {name} {phase}` (phase `preview`, `execute` or `query`) on the `AppTemplate.Actions` ActivitySource. Tags: `action.name`, `action.phase`, `action.outcome` (`success` or the error code), `aggregate.type`, `aggregate.id`, `preview.stale_reason`. The `AppTemplate.Actions` Meter counts calls and durations.
- Integration tests write each request's spans and response body to the test output, shown when a test fails.
- The app's own MCP server (`app` in `.mcp.json`) lets you try a feature as a runtime agent would, once the AppHost is running.

## Frontend rules

- Add components with `npx shadcn@latest add <component>`, run from `src/web`. Never edit `src/web/src/components/ui/*`; compose on top.
- Tailwind tokens only. No custom CSS.
- Lists use `<DataTable>` (`src/web/src/components/data-table.tsx`). Every mutation goes through `<PreviewConfirmDialog>` (`src/web/src/components/preview-confirm-dialog.tsx`); errors render with `<ProblemAlert>`.
- Call the Api only through the typed client (`src/web/src/api/client.ts`, `src/web/src/api/actions.ts`). Never hand-edit `src/web/src/api/schema.d.ts`; regenerate it with `npm run gen:api`.
- The `shadcn` MCP server starts at the repo root, where there is no `components.json`. Pass `registries: ["@shadcn"]` to its search and list tools, and run the commands it returns from `src/web`.
- Procedure: `skill:frontend-feature`.

## Docs-lookup rule

Before using any framework or third-party API, look it up. Never write it from memory.

1. .NET, ASP.NET Core, EF Core and Aspire: the Microsoft Learn MCP server (bundled with the `microsoft-docs` plugin).
2. OneOf, the MCP C# SDK, FluentValidation, Shouldly, shadcn/ui, Tailwind and TanStack: Context7 (`context7` in `.mcp.json`).
3. Then confirm against the installed package before building on it: the restored package in `~/.nuget/packages/<id>/<version>/` (XML docs or source), `src/web/node_modules/<package>`, or the tool's `-h`.

## dotnet-claude-kit overrides

dotnet-claude-kit is the base. Where it conflicts with this file, this file wins.

| Kit says | This template does |
|---|---|
| Hand-rolled `Result` / `Result<T>` | **OneOf** everywhere. Never introduce a `Result` type |
| FluentValidation via `ValidationFilter<T>` / Mediator `ValidationBehavior`, which throw `ValidationException` | FluentValidation run by the action registry with `ValidateAsync`, **never throwing**. Value objects own the rules through `MustBeValid` |
| Mediator / MediatR `IRequest` + handlers; `IEndpointGroup` endpoints; `/scaffold` for features | **The action registry.** One `[Action]`/`[Query]` class generates the HTTP + MCP + preview. Never hand-write endpoints or MCP tools for actions. **No `/scaffold`**: use `skill:add-action` |
| Domain events implement Mediator `INotification` | Our own `IDomainEvent`, handled by `IDomainEventHandler<T>` after commit. Domain depends only on OneOf |
| Integration tests on Testcontainers Postgres | In-memory **SQLite** with real migrations. EF `InMemory` stays banned, as in the kit |
| Clean Architecture in 4 projects (Domain/Application/Infrastructure/Api) | **Domain + Api** (see the architecture map). Judge `/arch-check` against that map |
| Value objects validate in the constructor | Private constructor + `Create` returning OneOf |
| Assertions with xUnit `Assert` | **Shouldly** only. No `Assert.*`, no FluentAssertions |
| `dotnet test` with VSTest options; piping into `hooks/post-test-analyze.sh` | **MTP commands**: `dotnet test --solution AppTemplate.slnx` or `--project … --filter …`; exit 8 = no tests matched. That hook script ships in the plugin, not this repo |
| ProblemDetails for errors | Same, plus the required extensions `code`, `details`, `suggestion` and `traceId` |
| Aspire examples with `AddPostgres` / `AddNpgsqlDbContext` | `AddSqlite` for v1; Postgres only via `skill:swap-to-postgres` |
| `/dotnet-init` to set up a project | **Never run `/dotnet-init`.** It overwrites `CLAUDE.md`, which must stay `@AGENTS.md` |
| (official `aspire` plugin) run `aspire agent init` | **Never run `aspire agent init`.** It writes hooks into user-level `~/.claude/settings.json`. The Aspire MCP server is already in `.mcp.json` |
| Async suffix on every async method; one type per file; `internal` by default | Action convention methods are named exactly `Load`, `Apply` and `Run`. An action file holds its input record and handler; validators are grouped in `<Aggregate>Validators.cs`. Handlers, inputs, validators and errors are `public` |
| `[Authorize]`/`[AllowAnonymous]` on every endpoint; `dotnet add package Mediator…` examples | No auth in v1, and endpoints are generated. Never add Mediator or MediatR |

The edited kit rules in `.claude/rules/` (`architecture.md`, `error-handling.md`, `testing.md`) carry the same overrides.

**Where the kit agrees, follow it** (skills `dotnet-claude-kit:ef-core`, `dotnet-claude-kit:testing`, `dotnet-claude-kit:opentelemetry`, `dotnet-claude-kit:logging`, `dotnet-claude-kit:modern-csharp`): DbContext as the unit of work with no repositories, `IEntityTypeConfiguration<T>`, no lazy loading, `.Select()` projections, never auto-migrating in production, xUnit v3, `FakeTimeProvider`, `IMeterFactory`, `[LoggerMessage]`, low-cardinality tags and strongly typed ids.

## Plugins and prerequisites

`.claude/settings.json` declares these plugins. After you trust this folder, Claude Code registers their marketplaces, but each developer installs the plugins once: `claude plugin install <plugin>@<marketplace> --scope user` for each row, then `/reload-plugins`.

| Plugin | Marketplace (repo) | Use it for |
|---|---|---|
| `dotnet-claude-kit` | `dotnet-claude-kit` (`codewithmukesh/dotnet-claude-kit`) | General .NET skills, agents, commands, hooks, the `cwm-roslyn-navigator` MCP server |
| `aspire` | `aspire-skills` (`microsoft/aspire-skills`) | Aspire 17; `aspire-monitoring` for logs and traces. Wins over the kit's `aspire` skill |
| `dotnet` | `dotnet-agent-skills` (`dotnet/skills`) | C# LSP, `csharp-refactoring`, `dotnet-breaking-changes` |
| `dotnet-data` | `dotnet-agent-skills` (`dotnet/skills`) | `optimizing-ef-core-queries` |
| `microsoft-docs` | `microsoft-docs-marketplace` (`microsoftdocs/mcp`) | The Microsoft Learn MCP server and docs skills |

Install once per machine:

- **Aspire CLI**, for the `aspire` MCP server: `curl -sSL https://aspire.dev/install.sh | bash`, or `dotnet tool install -g Aspire.Cli`.
- **Roslyn navigator**, for the kit's MCP server: `dotnet tool install -g CWM.RoslynNavigator`.
- **Context7 API key** (optional; raises rate limits). Context7 works without one. Keep the key in the `CONTEXT7_API_KEY` environment variable and never commit it. To use it, add a local-scope override of the same server: `claude mcp add --scope local --transport http --header "Authorization: Bearer $CONTEXT7_API_KEY" context7 https://mcp.context7.com/mcp`.

Other agents don't get the plugins. They still get this file, `.claude/rules/` and the repo-owned skills in `.claude/skills/`.

## Never run

- `aspire agent init`: writes telemetry hooks into the user-level `~/.claude/settings.json`, even with `--scope project`.
- `/dotnet-init`: overwrites `CLAUDE.md`.
- `git push --force`, `git reset --hard`, `rm -rf`, `pkill`: the kit's bash guard blocks them. Don't work around it.

## Swap to Postgres

v1 targets SQLite. To move to Postgres, follow `skill:swap-to-postgres`: change the AppHost resource to `AddPostgres(...).AddDatabase("appdb")`, register `UseNpgsql`, and regenerate the migrations as a new initial migration. The `NOCASE` collation on `ProjectName` (`src/AppTemplate.Api/Data/ProjectConfiguration.cs`) is SQLite-specific and must change too.
