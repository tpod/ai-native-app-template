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
  AppTemplate.AppHost/          # Aspire: Postgres appdb (Podman), api, Vite web
  AppTemplate.ServiceDefaults/  # OpenTelemetry, health checks, resilience
  AppTemplate.Domain/           # aggregates, entities, value objects, errors, events. References only OneOf
    Common/                     # AggregateRoot, AppError, ErrorAttribute, IValueObject, IDomainEvent, ValidationFailed
    Projects/                   # the example aggregate (remove with skill:remove-example)
  AppTemplate.Api/
    Actions/                    # framework: registry, pipeline, preview token, validation bridge, Http/, Telemetry/
    Features/<Aggregate>/       # one file per action or query, plus <Aggregate>Validators.cs
    Features/History/           # get_history: every aggregate's business history
    Data/                       # AppDbContext, EF configurations, ValueObjectConventions, Migrations/
    Mcp/                        # MCP wiring and server instructions; tools are generated from the registry
  web/                          # React + Vite + TypeScript, Tailwind v4, shadcn/ui, TanStack Query + Table
tests/
  AppTemplate.Api.Tests/        # WebApplicationFactory integration tests (HTTP + MCP)
    Infrastructure/             # ApiFactory, ApiTestBase, ApiResponseShouldly, CoversAttribute
    Fixtures/Notes/             # fixture_* actions for framework tests
    Features/<Aggregate>/       # one {ActionClass}Tests per action
  AppTemplate.Api.PostgresTests/   # provider behaviour on real Postgres (Testcontainers on Podman)
  AppTemplate.Architecture.Tests/  # layering, banned types, error codes, [Covers] coverage, provider models
```

## Golden rules

1. **Define each action once** with `[Action("snake_name", "description")]` + `IMutation<TInput, TAggregate>`, or `[Query(...)]` + `IQuery<TInput, TOutput>`, in `src/AppTemplate.Api/Features/<Aggregate>/`. The OneOf error arguments of `Load`/`Apply`/`Run` are the declared error codes. Names never start with `preview_`, which is reserved for the generated preview tools. Recipe: `skill:add-action`.
2. **OneOf only.** Domain methods and actions return `OneOf<…>`. Never introduce a C# `Result`/`Result<T>` type.
3. **Value objects own their rules.** Private constructor, `static Create(...)` returning `OneOf<TVo, ValidationError>`. Single-value VOs implement `IValueObject<TSelf, TValue>` and are mapped by convention (`ValueObjectConventions`). See `skill:domain-modeling`.
4. **Every action gets a `{ActionClass}Tests` class with `[Covers]`** for the happy path, each error code and, for mutations, the stale case. See `skill:tdd`.
5. **Never hand-write endpoints or MCP tools** for actions. No controllers, `MapPost`, `IEndpointGroup` or `[McpServerTool]`.
6. **No provider-specific SQL in domain or feature code.** Provider details stay in `src/AppTemplate.Api/Data`. See `skill:ef-core`.
7. **Validation never throws.** FluentValidation `AbstractValidator<TInput>` in the Api, using `RuleFor(x => x.Name).MustBeValid(ProjectName.Create)`, `.WithSuggestion("…")` and `.WithErrorCode("…")`. Never `ValidateAndThrow` or `ValidationException`.
8. **Behavior lives on the aggregate.** No public setters. Actions load data, including DB-dependent checks such as uniqueness, and pass it into the domain method. Every method that changes state raises a domain event with `[Event("<aggregate>.<verb>", 1)]`; a change without one fails as `internal.unexpected`.
9. **Changing an action's input, output or error codes changes an MCP contract.** Follow `skill:change-action-contract`.
10. **Every state change goes through an action.** Imports, scheduled jobs and admin tasks call an action through `ActionRunner`. They never change aggregates or call `SaveChanges` themselves. This is what keeps history, validation, stale checks and telemetry complete. A new project containing state-changing code must be added to `StateChangeTests`.

`tests/AppTemplate.Architecture.Tests` enforces these mechanically. It bans `ValidationException`, Mediator, MediatR, any `Result`/`Result<T>` type (including `OneOf.Types.Result`) and the EF InMemory provider. It also requires `IAggregateRoot.Version` to be a concurrency token, and checks layering, error codes, `[Event]` names, `[Covers]` coverage and that only `ActionPipeline` saves (`StateChangeTests`).

**Test rules** (details in `.claude/rules/testing.md`):

- Mark coverage with `[Covers("rename_project", "project.name_taken")]` or `[Covers("rename_project", Case.HappyPath)]` / `Case.Stale`. `Case` is globally aliased in `tests/AppTemplate.Api.Tests/GlobalUsings.cs` (Shouldly has its own `Case`).
- `CoverageTests` fails unless every action has HappyPath, every declared error code (including `validation.failed` when a validator exists) and, for mutations, Stale.
- Shouldly only, names `Method_State_Expected`, and pass `TestContext.Current.CancellationToken` where the analyzer asks.
- Framework tests (`Actions/`, `Mcp/`, `OpenApi/`, `Telemetry/`, `Http/`, `Infrastructure/`) use only the `fixture_*` Note actions in `tests/AppTemplate.Api.Tests/Fixtures/Notes` and never reference the Projects example. Register fixtures through `ActionRegistryOptions.Types` (`NoteFixture.Register`), never by scanning the Api.Tests assembly: it holds deliberately invalid handlers.

## Adding a feature

Detail: `skill:add-action`. Write the tests first (`skill:tdd`).

1. Value object(s) in `src/AppTemplate.Domain/<Aggregate>/`: private constructor, `Create` returning OneOf.
2. Aggregate behavior returning OneOf, plus its `AppError` records with `[Error]`.
3. Validator in `<Aggregate>Validators.cs`: `MustBeValid(Vo.Create)` plus input-only rules.
4. The `[Action]` or `[Query]` class in `src/AppTemplate.Api/Features/<Aggregate>/`.
5. EF mapping in `src/AppTemplate.Api/Data/` and a migration (see Commands).
6. `{ActionClass}Tests` in `tests/AppTemplate.Api.Tests/Features/<Aggregate>/` with `[Covers]` for HappyPath, each error code and Stale.
7. `scripts/update-openapi.sh`, then `cd src/web && npm run gen:api`.
8. Optional UI: a page using `<DataTable>` and `<PreviewConfirmDialog>` (`skill:frontend-feature`).
9. `scripts/verify.sh` must print `VERIFY OK`.

Skill map:

| Task | Skill |
|---|---|
| New action or query | `skill:add-action` |
| New aggregate root | `skill:add-aggregate` |
| Value objects, domain errors, events | `skill:domain-modeling` |
| EF mapping, migrations | `skill:ef-core` |
| Tests first, `[Covers]` | `skill:tdd` |
| Change an action's input, output or error codes | `skill:change-action-contract` |
| UI page | `skill:frontend-feature` |
| Debug a failure | `skill:investigate` |
| Delete the Projects example | `skill:remove-example` |
| Rename the template | `skill:rename-template` |

## Preview protocol

Every mutation is previewed first. Over MCP: `preview_<name>` → show the user `summary` and `changes` (and `warnings`) → get confirmation → `<name>` with the same input plus `previewToken`. Over HTTP: `POST /api/actions/<name>/preview`, then `POST /api/actions/<name>` with the input fields plus `previewToken`. Queries are `POST /api/queries/<name>` and need no preview.

MCP `structuredContent` is always a JSON object: a list result arrives as `{ "items": [...] }` and a scalar as `{ "value": x }`. HTTP returns the bare value.

- A preview saves nothing. The token binds the input, the loaded aggregate versions and the change set. It lasts 300 s by default (`PreviewLifetimeSeconds` on `[Action]`) and works once.
- `preview.stale` or `preview.expired` → call the preview again and show the new result. `details.reason` on stale is `version_changed`, `domain_error`, `changes_differ` or `concurrent_write`.
- `preview.already_executed` (a create whose id already exists) → don't retry; query to confirm.
- A no-op mutation previews empty `changes` and does not bump `Version`.
- An action may change several aggregates in one execute (prefer one; use more only when one rule spans them). Each changed aggregate gets a new `Version` and must raise its own event, or the call fails as `internal.unexpected`. A change to a child entity counts against the root it belongs to.
- The UI follows the same flow through `<PreviewConfirmDialog>`.
- Execute accepts an optional `reason` (string, at most 500 characters) beside `previewToken`. It is stored in history and not bound by the token. No input record may declare a `reason` property.

## History

Every committed execute stores each domain event it raised as an append-only row in the `History` table, in the same transaction as the aggregate change. A row holds the event's `[Event]` name and version, the payload, the aggregate type, id and version, the action, the channel (`http`, `mcp` or `in_process`), the MCP client when known, the caller's `reason` and the `traceId`.

- Read it with the `get_history` query (`aggregateType`, `aggregateId`, optional `limit`), oldest first paged with `afterId`, or `newestFirst: true` paged back with `beforeId`.
- Event names are the history contract: never rename one; bump its version when the payload changes. `EventNameTests` checks they are unique and well formed.
- A preview, a stale execute and a failed execute write nothing.

## Errors

Every error is RFC 9457 ProblemDetails (`application/problem+json`), the same JSON on HTTP and on MCP (`isError: true`): `type`, `title`, `status`, `detail`, `instance`, plus `code`, `details`, `suggestion` and `traceId`. Every response carries an `X-Trace-Id` header.

- Define errors as `sealed record`s deriving from `AppError` with `[Error("area.snake_case", ErrorKind.X, "Stable title")]`. See `src/AppTemplate.Domain/Projects/ProjectErrors.cs`.
- Codes match `^[a-z_]+\.[a-z_]+$` and are unique. Status comes from `ErrorKind`: Validation 422, NotFound 404, Conflict 409, Precondition 409, BadRequest 400, Forbidden 403, Unexpected 500.
- `validation.failed` lists `details.fields[]` with `{ field, code, message, suggestion }`. Field codes come from the VO error (`project_name.too_long`) or `.WithErrorCode`.
- A good `suggestion` is the next step an agent can take, and names a tool when one helps: "Choose a different name; list_projects shows existing names." It never just restates the error.
- Framework codes: `request.malformed`, `request.not_found`, `request.forbidden_origin`, `internal.unexpected`, `preview.token_invalid`, `preview.token_mismatch`, `preview.expired`, `preview.stale`, `preview.already_executed`.

## Security

- **Origin check (DNS-rebinding protection).** `OriginCheckMiddleware` (`src/AppTemplate.Api/Actions/Http/OriginCheckMiddleware.cs`) guards `/api` and `/mcp`. A request with an `Origin` header passes only from a loopback origin (`http(s)://localhost`, `127.0.0.1` or `[::1]`, any port) or an origin listed in `Security:AllowedOrigins`. Anything else gets `403 request.forbidden_origin`. Requests without `Origin` (agents, MCP clients, curl) pass. The Vite dev proxy forwards the browser's localhost `Origin`, so the dev UI passes.
- **Production.** Add the app's public origin, for example `https://app.example.com`, to `Security:AllowedOrigins` (env `Security__AllowedOrigins__0`), or the deployed UI gets 403. Set `AllowedHosts` to the real host names. Development already restricts it to `localhost;127.0.0.1` in `appsettings.Development.json`.
- Auth is deferred in v1. Don't expose the Api beyond localhost without adding it.

## Commands

| Task | Command |
|---|---|
| Is it green? Run before claiming done | `scripts/verify.sh` (must print `VERIFY OK`) |
| Is it green, without Postgres (prints `POSTGRES TESTS SKIPPED`) | `VERIFY_POSTGRES=0 scripts/verify.sh` |
| Run everything (dashboard, Postgres `appdb` on Podman, `api`, Vite `web`) | `dotnet run --project src/AppTemplate.AppHost` |
| Full test suite (the Postgres project needs `DOCKER_HOST`, see "Databases") | `dotnet test --solution AppTemplate.slnx` |
| Postgres tests only | `DOCKER_HOST=unix://$XDG_RUNTIME_DIR/podman/podman.sock dotnet test --project tests/AppTemplate.Api.PostgresTests` |
| Focused tests | `dotnet test --project tests/AppTemplate.Api.Tests --filter "FullyQualifiedName~RenameProjectTests"` |
| Add a migration, for both Postgres and SQLite | `scripts/add-migration.sh <Name>` |
| After changing an action's contract | `scripts/update-openapi.sh`, then `cd src/web && npm run gen:api` |
| Rename the template | `scripts/rename.sh <NewName>` (see `skill:rename-template`) |

- `scripts/verify.sh` runs `dotnet build` (warnings are errors), `dotnet test`, the OpenAPI → TypeScript client drift check, then `npm run typecheck`, `npm run lint` and `npm run build` in `src/web`.
- Tests run on Microsoft.Testing.Platform with xUnit v3. Exit code 8 means zero tests matched: treat it as a failure, not a pass. Check `$?`.
- `dotnet run` uses the AppHost's first launch profile, `http`, which sets `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`. The dashboard is on `http://localhost:15165`; the console prints its login link. The Api is on `http://localhost:5180`, with MCP at `http://localhost:5180/mcp`, OpenAPI at `/openapi/v1.json` and health at `/health`.
- If NuGet restore hangs, set `DOTNET_SYSTEM_NET_DISABLEIPV6=1`. The repo scripts already default it.

## Observability and debugging

Debug in this order (`skill:investigate`): error `code` → `traceId` → trace and logs in the Aspire dashboard or the `aspire` MCP server → the failing test's output. When an aggregate is in an unexpected state, start with `get_history`: it is the durable record, and each row's `traceId` leads to the trace while it is retained.

- Every action phase is a span named `action {name} {phase}` (phase `preview`, `execute` or `query`) on the `AppTemplate.Actions` ActivitySource. Tags: `action.name`, `action.phase`, `action.outcome` (`success` or the error code), `aggregate.type`, `aggregate.id`, `preview.stale_reason`. The `AppTemplate.Actions` Meter counts calls and durations.
- Integration tests write each request's spans and response body to the test output, shown when a test fails.
- The app's own MCP server (`app` in `.mcp.json`) lets you try a feature as a runtime agent would, once the AppHost is running.

## Frontend rules

- Add components with `npx shadcn@latest add <component>`, run from `src/web`. Never edit `src/web/src/components/ui/*`; compose on top.
- Tailwind tokens only. No custom CSS.
- Lists use `<DataTable>` (`src/web/src/components/data-table.tsx`). Every mutation goes through `<PreviewConfirmDialog>` (`src/web/src/components/preview-confirm-dialog.tsx`); errors render with `<ProblemAlert>`.
- Every entity a page shows gets a `<HistoryButton>` (`src/web/src/components/history-button.tsx`). A child entity passes its root's type and id plus a `filter` on its own id key, e.g. `{ key: 'taskId', value: task.id }`.
- Call the Api only through the typed client (`src/web/src/api/client.ts`, `src/web/src/api/actions.ts`). Never hand-edit `src/web/src/api/schema.d.ts`; regenerate it with `npm run gen:api`.
- The `shadcn` MCP server starts at the repo root, where there is no `components.json`. Pass `registries: ["@shadcn"]` to its search and list tools, and run the commands it returns from `src/web`.
- Procedure: `skill:frontend-feature`.
- **Refreshing the shadcn skill.** The official shadcn skill (`skill:shadcn`) is vendored in `.claude/skills/shadcn/`, not installed as a plugin, so it does not update by itself. To refresh it, run `npx -y skills add shadcn/ui --skill shadcn --agent claude-code --copy -y` from the repo root. Read the diff before committing; it is third-party content. `skills-lock.json` records the upstream commit. Its `npx shadcn@latest info --json` context command finds no `components.json` at the repo root, so run it from `src/web` when you need the project context.

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
| Integration tests on Testcontainers Postgres | In-memory **SQLite** with real migrations for the fast suite; Testcontainers Postgres only in `tests/AppTemplate.Api.PostgresTests`, for provider behaviour. EF `InMemory` stays banned, as in the kit |
| Clean Architecture in 4 projects (Domain/Application/Infrastructure/Api) | **Domain + Api** (see the architecture map). Judge `/arch-check` against that map |
| Value objects validate in the constructor; positional records (public constructor) for value objects | `sealed` record with a private constructor + `Create` returning OneOf (golden rule 3). `ValueObjects_AreSealedWithoutPublicParameterizedConstructors` enforces it. Positional records stay fine for DTOs and inputs |
| Assertions with xUnit `Assert` | **Shouldly** only. No `Assert.*`, no FluentAssertions |
| `dotnet test` with VSTest options; piping into `hooks/post-test-analyze.sh` | **MTP commands**: `dotnet test --solution AppTemplate.slnx` or `--project … --filter …`; exit 8 = no tests matched. Don't pipe test output into the hook script (it isn't in this repo, and the pipe hides the exit code); read `dotnet test`'s own exit code, as `scripts/verify.sh` does |
| ProblemDetails for errors | Same, plus the required extensions `code`, `details`, `suggestion` and `traceId` |
| Aspire examples with `AddPostgres` / `AddNpgsqlDbContext` | `AddPostgres` in the AppHost, but `AddDbContext<AppDbContext, PostgresAppDbContext>` + `UseNpgsql` in the Api, not `AddNpgsqlDbContext` (it pools contexts, and `AppDbContext` takes `IModelContributor`s). See "Databases" |
| `/dotnet-init` to set up a project | **Never run `/dotnet-init`.** It overwrites `CLAUDE.md`, which must stay `@AGENTS.md` |
| (official `aspire` plugin) run `aspire agent init` | **Never run `aspire agent init`.** It writes hooks into user-level `~/.claude/settings.json`. The Aspire MCP server is already in `.mcp.json` |
| Async suffix on every async method; one type per file; `internal` by default | Action convention methods are named exactly `Load`, `Apply` and `Run`. An action file holds its input record and handler; validators are grouped in `<Aggregate>Validators.cs`. Handlers, inputs, validators and errors are `public` |
| `[Authorize]`/`[AllowAnonymous]` on every endpoint; `dotnet add package Mediator…` examples | Auth is deferred in v1, and endpoints are generated. Never add Mediator or MediatR |
| HTTPS everywhere, no exceptions | Local dev runs over http through the AppHost's `http` profile (`ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`). Production must terminate TLS |
| Branch prefixes `feature/`, `fix/`, `refactor/` | Conventional Commit types as branch prefixes: `feat/…`, `fix/…`, `refactor/…` |
| `/verify` or `dotnet build` + `dotnet test` before a PR | `scripts/verify.sh` is required: it also checks client drift and the web build |
| "Check AGENTS.md for the routing table / skill maps" | Use the skill map under "Adding a feature". There is no agent routing table; the kit's agents work as its plugin describes |

The edited kit rules in `.claude/rules/` (`architecture.md`, `error-handling.md`, `testing.md`) carry the same overrides.

**Where the kit agrees, follow it** (skills `dotnet-claude-kit:ef-core`, `dotnet-claude-kit:testing`, `dotnet-claude-kit:opentelemetry`, `dotnet-claude-kit:logging`, `dotnet-claude-kit:modern-csharp`): DbContext as the unit of work with no repositories, `IEntityTypeConfiguration<T>`, no lazy loading, `.Select()` projections, never auto-migrating in production, xUnit v3, `FakeTimeProvider`, `IMeterFactory`, `[LoggerMessage]`, low-cardinality tags and strongly typed ids.

## Plugins and prerequisites

`.claude/settings.json` declares these plugins. After you trust this folder, Claude Code registers their marketplaces, but each developer installs the plugins once per clone: `claude plugin install <plugin>@<marketplace> --scope project` for each row, then `/reload-plugins`. Project scope enables them only in this repo's `.claude/settings.json`, which already lists them, so nothing changes in `~/.claude/settings.json` or in other repos. Never use `--scope user` (the default): it turns the kit's hooks on in every repo.

| Plugin | Marketplace (repo) | Use it for |
|---|---|---|
| `dotnet-claude-kit` | `dotnet-claude-kit` (`codewithmukesh/dotnet-claude-kit`) | General .NET skills, agents, commands, hooks, the `cwm-roslyn-navigator` MCP server |
| `aspire` | `aspire-skills` (`microsoft/aspire-skills`) | Aspire 17; `aspire-monitoring` for logs and traces. Wins over the kit's `aspire` skill |
| `dotnet` | `dotnet-agent-skills` (`dotnet/skills`) | C# LSP, `csharp-refactoring`, `dotnet-breaking-changes` |
| `dotnet-data` | `dotnet-agent-skills` (`dotnet/skills`) | `optimizing-ef-core-queries` |
| `microsoft-docs` | `microsoft-docs-marketplace` (`microsoftdocs/mcp`) | The Microsoft Learn MCP server and docs skills |

Install once per machine:

- **Podman**, the container runtime for the AppHost's Postgres and the Postgres tests: see "Databases".
- **Aspire CLI**, for the `aspire` MCP server: `curl -sSL https://aspire.dev/install.sh | bash`, or `dotnet tool install -g Aspire.Cli`.
- **Roslyn navigator**, for the kit's MCP server: `dotnet tool install -g CWM.RoslynNavigator`.
- **Context7 API key** (optional; raises rate limits). Context7 works without one. Keep the key in the `CONTEXT7_API_KEY` environment variable and never commit it. To use it, add a local-scope override of the same server: `claude mcp add --scope local --transport http --header "Authorization: Bearer $CONTEXT7_API_KEY" context7 https://mcp.context7.com/mcp`.

Other agents don't get the plugins. They still get this file, `.claude/rules/` and the repo-owned skills in `.claude/skills/`.

## Never run

- `aspire agent init`: writes telemetry hooks into the user-level `~/.claude/settings.json`, even with `--scope project`.
- `/dotnet-init`: overwrites `CLAUDE.md`.
- `git push --force`, `git reset --hard`, `rm -rf`, `pkill`: the kit's bash guard blocks them. Don't work around it.

## Databases

Postgres is the only runtime database: `Program.cs` registers `PostgresAppDbContext` with `UseNpgsql`. SQLite exists only for the fast test suite: `ApiFactory` swaps in `SqliteAppDbContext` on an in-memory connection. Everything injects the abstract `AppDbContext`.

- Each provider has its own migration set (`Data/Migrations/Postgres`, `Data/Migrations/Sqlite`). Add both with `scripts/add-migration.sh <Name>`; `ProviderModelTests` fails if one is missing or the two models differ.
- Configurations stay provider-neutral. Case-insensitive text is `.IsCaseInsensitive()`: `citext` on Postgres, `NOCASE` on SQLite (`src/AppTemplate.Api/Data/CaseInsensitiveText.cs`).
- Tests: almost everything belongs in the fast SQLite suite (`tests/AppTemplate.Api.Tests`). Put a test in `tests/AppTemplate.Api.PostgresTests` only when it checks behaviour that differs by provider: migrations, collations and column types, locking, concurrency. It uses one container per run; the migrations run once into a template database, and each test gets its own database cloned from it (`CREATE DATABASE … TEMPLATE`, `PostgresContainerFixture.CreateDatabaseAsync`). A test of the migrations themselves uses `CreateEmptyDatabaseAsync`.
- `scripts/verify.sh` runs the Postgres tests by default and fails with the setup command when no container runtime answers. `VERIFY_POSTGRES=0` skips them and prints `POSTGRES TESTS SKIPPED`.

**Podman setup, once per machine.** The AppHost (`ASPIRE_CONTAINER_RUNTIME=podman` in its launch profiles) and Testcontainers both use rootless Podman:

```bash
sudo apt-get install -y podman
systemctl --user enable --now podman.socket
podman pull docker.io/library/postgres:18.6   # optional: avoids a slow first pull timing out the AppHost
```

`scripts/verify.sh` sets `DOCKER_HOST=unix://$XDG_RUNTIME_DIR/podman/podman.sock` when it is unset and that socket exists. If the AppHost's `postgres` resource ends `FailedToStart` with `context deadline exceeded`, the image pull was too slow: pull it as above and run again. Local data lives in the Podman volume `apptemplate.apphost-<hash>-postgres-data`.
