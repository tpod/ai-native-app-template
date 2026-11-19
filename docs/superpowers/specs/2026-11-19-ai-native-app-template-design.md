# AI-Native Full-Stack App Template — Design

Date: 2026-11-19
Status: Draft for review (builder check done)

## 1. Intent

A ready-to-clone template for full-stack .NET + React applications whose primary users are AI agents, with humans second.

There are two audiences, and both are first-class:

1. **Runtime agents** use the running app over MCP. Every feature is an MCP tool. Every data change can be simulated before it is committed. Every failure tells the agent how to fix its own mistake.
2. **Coding agents** extend the template with new features. The code foundations, rules, `AGENTS.md`, skills and MCP servers must make the correct pattern the easiest pattern. Tests must catch drift mechanically instead of relying on agents to follow prose.

Humans review agent changes in the agent's chat by reading the preview. The MCP client's per-tool permission prompt is the human gate. Humans also use a secondary React UI that goes through the same actions and the same preview flow.

## 2. Goals

- **One definition per action.** An action class produces the HTTP endpoints, the MCP tools, the preview, OpenAPI entries and telemetry. A feature cannot ship without its MCP exposure.
- **A bound preview for every mutation.** Execute requires a token from preview and fails if anything changed in between.
- **Structured errors.** Every error is a ProblemDetails (RFC 9457) object. Its `title`/`detail` carry the message, and the extensions `code`, `details`, `suggestion` and `traceId` are always present. The shape is the same on HTTP and MCP.
- **Rich DDD domain.** Aggregates carry the behavior and value objects carry the validation. There are no anemic entities and no services that hold domain logic. Control flow uses OneOf, not exceptions.
- **Integration tests are mandatory** for every action. They use `WebApplicationFactory` and SQLite.
- **Observability good enough that an agent can diagnose issues by itself** from error responses, traces, logs and test output.
- **Aspire** orchestrates local runs. SQLite is an Aspire resource, so swapping to Postgres is a small, contained change.
- **React + Vite frontend** with shadcn/ui, Tailwind CSS and TanStack Table. The API serves it at first, and it can also be deployed as a static bundle.

## 3. Non-goals (v1)

- Authentication and authorization (deferred).
- Human approval inbox in the React app. Review happens in the agent's chat. The token design leaves room to add an inbox later.
- Server-enforced human confirmation (MCP elicitation). v1 relies on the client's tool-permission prompts.
- MCP Apps (UI delivered as MCP resources). The UI and MCP share actions; the UI is not embedded in MCP clients.
- Postgres at runtime. Docker is not available in the dev environment. The swap path is designed and documented but not exercised.
- Outbox or durable event delivery. Domain events are dispatched in-process after commit.
- Multi-tenancy, background jobs, file storage.

## 4. Anti-goals (failure even if it "works")

- A coding agent can add a mutation that has no preview, no MCP tool, no error mapping or no integration test, and the build stays green.
- Errors reach agents as bare strings, stack traces or HTTP status codes without a `code` and a `suggestion`.
- Domain logic leaks into action classes, endpoints or EF configuration, leaving the entities as property bags.
- What a human approved in a preview differs from what was committed.
- Conventions live only in prose that agents can ignore.
- Hidden magic that a coding agent cannot find. The single action registry is the one accepted piece of indirection, and it must be small, central and documented.

## 5. Constraints

- **Environment:** .NET 10 SDK (10.0.115), Node 22, Ubuntu 26.04 VM, no Docker, no passwordless sudo.
- **Aspire 17.x**, the current stable release (17.0.0, released 2026-11-17). The AppHost is SDK-based, and the Aspire CLI is installed via `curl -sSL https://aspire.dev/install.sh | bash`. The SQLite resource comes from `CommunityToolkit.Aspire.Hosting.SQLite`, on whichever line matches Aspire 17. The client uses the CommunityToolkit SQLite EF Core client integration, or plain `UseSqlite` with the Aspire-provided connection string. The builder must verify package versions and compatibility on NuGet before pinning.
- **Libraries:**
  - OneOf (NuGet `OneOf`) for all result types.
  - FluentValidation for input validation. It never throws (§7.5).
  - Shouldly for test assertions.
- **Build:** `TreatWarningsAsErrors=true` and central package management (`Directory.Packages.props`) across the repo.
- **Coding-agent base:** `codewithmukesh/dotnet-claude-kit` (MIT, v0.14.0+), with documented overrides where it conflicts with this design (§7.11).
- **MCP:** the official C# SDK, `ModelContextProtocol.AspNetCore`, using the Streamable HTTP transport.
- **Database:** SQLite for the running app and for tests.

## 6. Approaches considered

How "define an action once → HTTP + MCP + preview" is wired:

| Approach | Trade-off | Verdict |
|---|---|---|
| **A. Action classes + startup registry.** Reflection discovers `[Action]` classes and registers endpoints, MCP tools and OpenAPI. | One file per action, and skipping MCP or preview is impossible. Cost: one central piece of indirection. | **Chosen** |
| B. Same action classes plus a Roslyn source generator that emits endpoints and tools. | Generated code is visible and mistakes become compile-time errors. Cost: the generator is a project of its own, and agents debug generators poorly. | Too heavy for v1 |
| C. Explicit per-feature `Endpoint.cs` / `McpTool.cs` / handler, with architecture tests enforcing parity. | No hidden wiring. Cost: 3–4 boilerplate files per action, and gaps are caught only at test time. | Rejected |

How the preview is computed:

| Approach | Verdict |
|---|---|
| **Run the real domain method on the tracked aggregate, read the diff from EF `ChangeTracker`, collect domain events, discard. Never call `SaveChanges`.** | **Chosen.** Generic, and needs no preview code per action. |
| Execute inside a transaction, then roll back. | Rejected. SQLite allows only one writer, so previews would block writes, and side effects outside the DB can't be contained. |
| Hand-written preview per action. | Rejected. It drifts from the real behavior. |

Preview binding: **bound** (execute requires the token), chosen over advisory, because only a bound preview guarantees that what commits is exactly what was previewed.

Validation: **the value object owns each rule, and FluentValidation collects the failures** (chosen). A `MustBeValid(Vo.Create)` bridge runs the value object's factory inside the validator. It was chosen over "FluentValidation owns all rules", which would let invalid value objects be built anywhere outside the validator.

Error format: **ProblemDetails (RFC 9457) with extensions** (chosen) over a custom envelope. It's the HTTP standard and matches the dotnet-claude-kit skills.

Human gate over MCP: **rely on the client** (chosen), over server-enforced MCP elicitation. The token guarantees integrity. Whether a human sees the preview depends on the MCP client's per-tool permission prompts and on the agent following the server instructions. Elicitation was deferred because client support varies, and it would block unattended runs.

## 7. Design

### 7.1 Solution layout

```
AppTemplate.slnx
Directory.Build.props        # net10.0, Nullable, TreatWarningsAsErrors, analyzers
Directory.Packages.props     # central package management
.editorconfig
AGENTS.md  CLAUDE.md  .mcp.json
.claude/skills/...
scripts/verify.sh            # the single "is it green" command
src/
  AppTemplate.AppHost/          # Aspire: sqlite resource, api, vite dev server
  AppTemplate.ServiceDefaults/  # OTel, health checks, resilience
  AppTemplate.Domain/           # aggregates, entities, VOs, errors, events. No EF/ASP.NET refs.
  AppTemplate.Api/
    Actions/                    # registry, preview engine, token, validation bridge, ProblemDetails mapping (framework code)
    Features/<Aggregate>/       # one file per action/query, e.g. Projects/RenameProject.cs
    Data/                       # AppDbContext, EF configurations, migrations
    Mcp/                        # MCP server wiring (tools are generated from the registry)
    wwwroot/                    # built frontend (gitignored output)
  web/                          # React + Vite + TS
tests/
  AppTemplate.Api.Tests/        # WebApplicationFactory integration tests (HTTP + MCP)
  AppTemplate.Architecture.Tests/
```

Builder's call: project naming. The `AppTemplate` prefix is a placeholder; see §7.13.

### 7.2 Domain model rules

- `AggregateRoot` base class provides `Id`, `Version` (int, used as the EF concurrency token) and a domain-event list. Child entities are reachable only through their root.
- **Value objects** are immutable `sealed record`s (or `readonly record struct`s) with a private constructor. They have a static `Create(...)` that returns `OneOf<TVo, ValidationError>`. Ids are strongly typed VOs (`ProjectId`).
- **Behavior lives on the aggregate.** Methods such as `project.Rename(ProjectName name)` return precise OneOf results, e.g. `OneOf<Success, ProjectArchived>`. They raise domain events. Entities have no public setters.
- Checks that need the database (for example, name uniqueness) are loaded by the action and passed into the domain method as data or as a small domain interface. The decision stays in the domain.
- Domain errors are records implementing `IAppError` (§7.5).
- `AppTemplate.Domain` depends only on OneOf. It does not reference EF Core, ASP.NET Core or the Api. An architecture test enforces this.

### 7.3 Actions and the registry

Two kinds of operation:

- **Mutation:** changes data, and gets preview + execute.
- **Query:** read-only, a single call.

Illustrative shape (the builder may refine signatures while keeping the guarantees):

```csharp
[Action("rename_project", "Rename a project. Names are unique across non-archived projects.")]
public sealed class RenameProject : IMutation<RenameProjectInput, Project>
{
    public sealed record RenameProjectInput(Guid ProjectId, string Name);

    // Load the target aggregate (for a create: construct it and add it to the DbContext).
    public async Task<OneOf<Project, ProjectNotFound>> Load(RenameProjectInput input, ActionContext ctx) => ...;

    // Convert the input into VOs, call domain behavior. No persistence here.
    public async Task<OneOf<Success, ValidationFailed, ProjectNameTaken, ProjectArchived>> Apply(
        Project project, RenameProjectInput input, ActionContext ctx) => ...;

    // One-line human summary shown in the preview.
    public string Summarize(Project before, RenameProjectInput input) => $"Rename '{before.Name}' → '{input.Name}'";
}

[Query("list_projects", "List projects, optionally filtered by status.")]
public sealed class ListProjects : IQuery<ListProjectsInput, IReadOnlyList<ProjectDto>> { ... }
```

Registry responsibilities, run at startup:

1. Discover every `[Action]` and `[Query]` type in the configured assemblies. The default is the Api assembly; tests add their fixture assembly.
2. **Validate, and fail startup on violation:**
   - Every OneOf type argument other than the success type implements `IAppError`.
   - Names are unique snake_case.
   - Descriptions are non-empty.
   - Input types are records.
3. **Register HTTP endpoints:**
   - `POST /api/actions/{name}/preview` with body = input.
   - `POST /api/actions/{name}` with body = `{ ...input, previewToken }`.
   - `POST /api/queries/{name}` with body = input.
4. **Register MCP tools:**
   - `preview_{name}` and `{name}` for mutations, and `{name}` for queries.
   - JSON schema generated from the input record.
   - The tool description includes the action description, the list of possible error codes (read from the OneOf type arguments), and, for execute tools, "requires `previewToken` from `preview_{name}`".
   - Tool annotations: `readOnlyHint` on queries and previews, and `destructiveHint` when the action attribute says so.
5. **Register OpenAPI operations** with the input/output schemas and the ProblemDetails error schema, so the TypeScript client can be generated.

**Request pipeline for every call** (query, preview and execute):

1. Deserialize the request. Failure → `request.malformed`.
2. If an `IValidator<TInput>` is registered in DI, run `ValidateAsync`. Never call `ValidateAndThrow`. Failures → `validation.failed` (§7.5).
3. Then the mutation or query flow (§7.4).

`validation.failed` is implicitly a possible error of every action that has a validator. The registry adds it to the tool description, and it's covered by the `[Covers]` check.

The precise way the registry reads the error types (for example, from the `Apply` method's return type via reflection, with `IMutation<,>` as a marker plus convention-named methods) is the builder's call. The requirement is that **the declared OneOf error types are the single source of truth** for documentation, MCP descriptions and startup validation.

### 7.4 Preview engine and token

**Preview:**

1. Open a fresh `DbContext` scope.
2. Call `Load`. For creates, the new aggregate id is allocated here.
3. Call `Apply`.
4. On an error, return the ProblemDetails error.
5. On success, read the `ChangeTracker`:
   - per-field `before`/`after` for modified, added and deleted entities, excluding system-managed fields (`Version`, timestamps);
   - the raised domain events, as type name and payload.
6. Compute `changesHash` over the normalized change set.
7. Sign the token. Dispose the scope without saving.

Response:

```json
{
  "action": "rename_project",
  "summary": "Rename 'Alpha' → 'Beta'",
  "changes": [{ "entity": "Project", "id": "…", "op": "modified", "field": "name", "before": "Alpha", "after": "Beta" }],
  "sideEffects": [{ "event": "ProjectRenamed", "payload": { … } }],
  "warnings": [],
  "previewToken": "…",
  "expiresAt": "2026-11-19T12:05:00Z"
}
```

**Token:**

- Stateless. Its payload is `{ action, inputHash, aggregateVersions, changesHash, allocatedIds, expiresAt }`, signed with HMAC-SHA256 and base64url-encoded.
- `aggregateVersions` holds the `(type, id, Version)` of every aggregate root `Load` returned. **Binding is strict:** any change to a loaded aggregate between preview and execute makes the preview stale, even a change that doesn't conflict (for example, `add_task` landing between a rename's preview and execute). This matches the agreed rule "if the entity changed in between, execute fails."
- The key comes from config `Preview:SigningKey`. The AppHost supplies a dev key as a parameter. Tests use a fixed key.
- Default lifetime is 5 minutes, overridable per action through the attribute.
- Time comes from an injected `TimeProvider`. Tests use `FakeTimeProvider`.

**Execute:**

1. Verify the signature. Failure → `preview.token_invalid`.
2. Check that the action name and `inputHash` match. Mismatch → `preview.token_mismatch`.
3. Check expiry. Expired → `preview.expired`.
4. Re-run `Load`. If any loaded root's `Version` differs from `aggregateVersions` → `preview.stale`, with suggestion "Call preview_{name} again and review the new preview."
5. Re-run `Apply`, reusing `allocatedIds` from the token. Recompute `changesHash`. If it differs → `preview.stale`. This catches creates and DB-dependent checks such as uniqueness, where no loaded root's version changed.
6. Increment the root's `Version` and call `SaveChanges`.
   - `DbUpdateConcurrencyException` → `preview.stale`.
   - A create whose allocated id already exists → `preview.already_executed`.
7. After commit, dispatch domain events in-process.
8. Return `{ result, changes }`.

Recomputing the change set makes creates and updates behave the same way. It also re-runs uniqueness and other DB-dependent checks on execute without any extra code.

**Warnings** (my call): actions add non-blocking warnings through `ActionContext.Warn(code, message)`, for example "project has 0 tasks". Warnings appear in the preview and are included in `changesHash`, so a change in warnings also makes the preview stale.

### 7.5 Structured errors

```csharp
public interface IAppError
{
    string Code { get; }          // "<area>.<snake_case>", e.g. "project.name_taken"
    string Message { get; }       // human-readable, specific to this instance
    ErrorKind Kind { get; }       // Validation | NotFound | Conflict | Precondition
    object? Details { get; }      // structured, e.g. { field, value, existingProjectId }
    string Suggestion { get; }    // what the agent should do next; may name tools
}
```

- **Wire format: ProblemDetails (RFC 9457) with extensions.** The same JSON is used on HTTP (`application/problem+json`) and MCP:
  ```json
  { "type": "https://errors.apptemplate.dev/project.name_taken",
    "title": "Project name already taken",
    "status": 409,
    "detail": "A non-archived project named 'Beta' already exists.",
    "instance": "/api/actions/rename_project",
    "code": "project.name_taken",
    "details": { "field": "name", "value": "Beta", "existingProjectId": "…" },
    "suggestion": "Choose a different name; list_projects shows existing names.",
    "traceId": "…" }
  ```
- **Mapping from `IAppError` to ProblemDetails:**

  | ProblemDetails field | Comes from |
  |---|---|
  | `title` | a stable per-code title, a static string on the error type |
  | `detail` | `Message`, specific to this instance |
  | `status` | `Kind`, via the table below |
  | `type` | built from `Code`. The base URI is configurable and doesn't need to resolve in v1 |

  The extensions `code`, `details`, `suggestion` and `traceId` are always present. Produce it with ASP.NET Core's `IProblemDetailsService` / `TypedResults.Problem`, so that framework-generated errors (404 on unknown routes, 415, and so on) share the shape. A customizer adds `code` and `suggestion` where they're missing, e.g. `request.not_found`.
- **HTTP status mapping:**

  | Error | HTTP status |
  |---|---|
  | `Validation` | 422 |
  | `NotFound` | 404 |
  | `Conflict` | 409 |
  | `Precondition` (preview token errors) | 409 |
  | `preview.token_invalid` | 400 |

- **On MCP**, errors are a tool result with `isError: true`, where structured content and text both carry the ProblemDetails JSON.
- **Validation: FluentValidation, never throwing.**
  - Each action may have an `AbstractValidator<TInput>`. The registry runs `ValidateAsync` and maps a failed `ValidationResult` to `ValidationFailed : IAppError`:
    - code `validation.failed`, status 422;
    - `details.fields[]` = `{ field, code, message, suggestion }`.
  - **Value objects own their rules.** The bridge `RuleFor(x => x.Name).MustBeValid(ProjectName.Create)` runs the value object's `Create` and turns its OneOf error into a field failure. The field `code` is the value object error's code (e.g. `project_name.too_long`), and the suggestion is its suggestion.
  - FluentValidation itself holds only rules that belong to the input: required fields, rules across fields, and formats with no value object.
  - Suggestions on plain FluentValidation rules come from `.WithSuggestion("…")`, a template extension that stores the text in `CustomState`. Field codes come from `.WithErrorCode("…")`. A rule without an explicit code gets a derived default, e.g. `project_id.not_empty`.
  - All failures are returned together, not only the first.
  - **Value objects are still created in `Apply`** through `Create`. After validation it can't fail, but domain code never trusts unvalidated primitives.
  - **Banned:** `ValidateAndThrow`, the dotnet-claude-kit `ValidationBehavior`, `ValidationFilter<T>`, and any `ValidationException` flow. An architecture test bans references to `ValidationException` in the Api assembly.
- **Unhandled exceptions** become `internal.unexpected`. The suggestion says the error is not caused by the input and points to the `traceId`. The full exception goes to logs and traces only, never to the response.
- Malformed JSON and model-binding failures become `request.malformed`.
- An architecture test asserts that error codes are unique across all `IAppError` types and match `^[a-z_]+\.[a-z_]+$`.

### 7.6 MCP server

- `ModelContextProtocol.AspNetCore`, Streamable HTTP at `/mcp`. Tools are created from the registry (§7.3), not with hand-written `[McpServerTool]` classes.
- No auth in v1.
- Server instructions (the MCP `instructions` field) explain the preview → execute protocol and the ProblemDetails error format. Agents learn the rules from the server itself.
- **Human gate (v1, relies on the client):**
  - Execute tools are annotated `readOnlyHint: false`, with `destructiveHint` where the action is flagged, so MCP clients prompt before calling them.
  - Both the server instructions and every execute tool description say: "Show the user the preview `summary` and `changes` and get their confirmation before calling this tool."
  - There is no server-side enforcement in v1. A later option is MCP elicitation per action.

### 7.7 Data

- `AppDbContext` lives in `Api/Data`. Mapping uses `IEntityTypeConfiguration<T>`, with backing fields for collections and private setters.
- Single-value VOs are mapped with value converters. Multi-value VOs use EF complex types.
- `Version` is configured as a concurrency token.
- EF Core migrations target SQLite and live in `Api/Data/Migrations`. The app applies migrations at startup in Development, and tests apply them per test database.
- AppHost: `builder.AddSqlite("appdb")` from CommunityToolkit, referenced by the Api. The Api reads the connection string `ConnectionStrings:appdb`.
- **Swap to Postgres (documented in `AGENTS.md`, not done in v1):**
  - Change the AppHost resource to `AddPostgres(...).AddDatabase("appdb")`.
  - Change the provider registration to `UseNpgsql`.
  - Regenerate the migrations as a new initial migration.
  - Rule enforced by review and docs: no raw or provider-specific SQL in domain or feature code.

### 7.8 Observability

- **Logs, traces, metrics:** `ServiceDefaults` sets up OpenTelemetry (ASP.NET Core, HttpClient, EF Core instrumentation) and exports OTLP to the Aspire dashboard.
- **Action spans:** an `ActivitySource("AppTemplate.Actions")` wraps every phase. Each span is named `action {name} {phase}`, with phase = preview, execute or query. Span tags:
  - `action.name` and `action.phase`;
  - `action.outcome`: `success`, or the error code;
  - `aggregate.type` and `aggregate.id`;
  - `preview.stale_reason` when relevant.
- **Action metrics:** a call counter and a duration histogram, tagged by name, phase and outcome.
- **Logs:** structured logging with stable message templates. Every error response is logged once at the boundary with its code and `traceId`.
- **`traceId` everywhere:** it appears in every error response, and every response carries a `traceparent`/`traceId` response header.
- **Aspire MCP for coding agents:** the Aspire CLI's MCP server (`aspire agent mcp`, stdio) is registered in `.mcp.json`, so coding agents can query resources, logs and traces. The `aspire-monitoring` skill from the Aspire plugin (§7.11) covers how to use it.
- **Test diagnostics:**
  - Integration tests route `ILogger` output to xUnit's test output.
  - An in-memory OTel exporter captures spans per test, and on assertion failure the test prints the spans for the failing request.
  - A test asserts that every action emits its span with an outcome tag.
- The `investigate` skill (§7.11) documents the order to debug in: error code → `traceId` → trace → logs → failing test.

### 7.9 Frontend

- **Stack:** `src/web` with Vite, React and TypeScript (strict), Tailwind CSS v4, shadcn/ui, TanStack Table and TanStack Query.
  - Router: React Router (builder's call).
  - Typed client: `openapi-typescript` + `openapi-fetch`, generated from the Api's OpenAPI document.
  - Client regeneration is part of `scripts/verify.sh`. A drifted client fails the check.
- **Shared components:**
  - `<DataTable>` is a wrapper on TanStack Table, used for every list.
  - `<PreviewConfirmDialog>` takes an action name and an input. It calls preview, renders the summary, the changes table, side effects and warnings, and calls execute with the token when confirmed. It also renders ProblemDetails errors, including `preview.stale` with a "re-preview" button. No mutation in the UI bypasses it.
- **Dev:** the AppHost runs the Vite dev server as a resource (Aspire's Vite/JavaScript hosting integration; the builder must verify the package for Aspire 17). Vite proxies `/api` and `/mcp` to the Api.
- **Serving:**
  - The Api's publish/build step runs `vite build` into `wwwroot`. The Api serves the static files and falls back to `index.html` for client routes.
  - The same `vite build` output is a standalone static bundle. The API base URL comes from `VITE_API_BASE_URL`, defaulting to same-origin.
- **Frontend checks:** `tsc --noEmit`, ESLint and `vite build` run in `verify.sh`. There are no frontend unit tests in v1.

### 7.10 Testing

- **Test stack:** xUnit v3, `WebApplicationFactory<Program>`, and **Shouldly** for all assertions. Plain xUnit `Assert.*` and FluentAssertions are not used; this overrides dotnet-claude-kit, which uses xUnit `Assert`.
- **`AppTemplate.Api.Tests`:**
  - Each test gets its own in-memory SQLite database. The connection is held open for the test's lifetime, and migrations are applied with `Database.Migrate()`.
  - EF's `InMemory` provider is banned.
  - `FakeTimeProvider` and a fixed signing key are used.
  - Helpers: `Preview(name, input)`, `Execute(name, input, token)`, `PreviewAndExecute(...)`, `Query(...)`.
  - Shouldly-style extensions on the ProblemDetails response: `response.ShouldFailWith("project.name_taken")`, and `ShouldHaveFieldError("name", "project_name.too_long")` for `validation.failed`.
  - Test naming follows dotnet-claude-kit: `Method_State_Expected`, with `[Covers]` alongside.
- **Mandatory per action:** a test class named `{ActionClass}Tests` covering:
  - the happy path through preview + execute;
  - each declared error code;
  - for mutations, a stale-preview case.
- **Coverage is enforced mechanically** (my call). Test methods carry `[Covers("rename_project", "project.name_taken")]`, or `[Covers("rename_project", Case.HappyPath)]` / `Case.Stale`. An architecture test reflects over the test assembly and fails when a registered action is missing any of these:
  - a happy-path case;
  - a case for each declared error code;
  - for mutations, a stale case.

  This is static and needs no ordering of test runs. The attribute could be wrong, but missing coverage can't pass silently.
- **Framework tests use test-only fixtures** (my call). Registry discovery takes a configurable list of assemblies, with the Api assembly as the default. The test project adds a fixture aggregate and fixture actions for the token, error and MCP-parity tests, wired through a test-only model and schema hook; the builder chooses the mechanism. So the framework tests don't depend on the Projects example, and they stay green after `remove-example`. Every test that loops over the registry asserts it is non-empty, and the fixture guarantees it is.
- **Framework tests**, written once:
  - token tampering, expiry, mismatch, stale and already-executed;
  - malformed requests;
  - unexpected exception → `internal.unexpected` with a `traceId`;
  - **MCP parity:** an MCP client connected to the test server lists the tools and asserts there is exactly one tool, or one preview/execute pair, per registry entry, with error codes in the descriptions. One action is also called end to end through MCP.
- **`AppTemplate.Architecture.Tests`** (ArchUnitNET or NetArchTest, builder's call):
  - Domain has no dependencies on EF, ASP.NET or Api.
  - Domain entities have no public setters.
  - VOs are immutable and sealed.
  - Every `[Action]`/`[Query]` has a matching `{Name}Tests` class in the test assembly.
  - Every OneOf error type argument implements `IAppError`.
  - Error codes are unique and well-formed.
  - No references to `ValidationException`, Mediator or MediatR, `Result<T>`-style types, or `Microsoft.EntityFrameworkCore.InMemory`.
  - The app boots, so the registry's startup validation passes.
- The collection-size rule applies: every test that loops over the registry or over error types first asserts the collection is non-empty.

### 7.11 Coding-agent tooling

- **`AGENTS.md`** is the single source of truth. It contains:
  - an architecture map;
  - the golden rules;
  - the checklist for adding a feature;
  - error-code conventions;
  - the preview protocol;
  - commands (`scripts/verify.sh`, run, migrations);
  - how to observe and debug;
  - frontend rules;
  - the Postgres swap path;
  - the dotnet-claude-kit overrides (§7.11.1);
  - per-machine prerequisites (Aspire CLI, `CWM.RoslynNavigator`).

  It stays short and links to the skills for procedures.
- **Docs-lookup rule in `AGENTS.md`.** Before using any framework or third-party API, look it up. Never write it from memory.
  - .NET, ASP.NET Core, EF Core and Aspire: the Microsoft Learn MCP.
  - OneOf, the MCP C# SDK, shadcn/ui, Tailwind and TanStack: Context7.
  - Then confirm against the installed package (its source, XML docs or `-h`) before building on it.
- **`CLAUDE.md`** contains `@AGENTS.md` and nothing else.

#### 7.11.1 Skills: base kit, plugins, repo-owned deltas

**Base: `codewithmukesh/dotnet-claude-kit`** (MIT, v0.14.0 or later), chosen by the user. It provides:
- 52 skills (`ddd`, `ef-core`, `testing`, `tdd`, `minimal-api`, `opentelemetry`, `modern-csharp`, …);
- 11 agents (`ef-core-specialist`, `test-engineer`, `code-reviewer`, …);
- 17 commands (`/verify`, `/tdd`, `/code-review`, `/build-fix`, `/migrate`, …);
- hooks: `dotnet format` on edit, `dotnet restore` after `.csproj` changes, and a bash guard that blocks force-push, `reset --hard`, `rm -rf` and `pkill`;
- the `cwm-roslyn-navigator` MCP server (26 Roslyn tools).

**Plugin references, not copies.** They stay current, and copies go stale and override the originals. The plugins are declared in the committed `.claude/settings.json` (`extraKnownMarketplaces` + `enabledPlugins`), so trusting the folder prompts the install. The cost is that each developer accepts the install, and the plugins work in Claude Code only; other agents still get `AGENTS.md`, `.claude/rules/` and the repo-owned skills.

| Plugin | Marketplace repo | Why it's included |
|---|---|---|
| `dotnet-claude-kit` | `codewithmukesh/dotnet-claude-kit` | The base: general .NET, EF Core, DDD, testing, workflow commands, hooks, Roslyn MCP |
| `aspire` | `microsoft/aspire-skills` | Official, and current for Aspire 17. The kit's `aspire` skill predates it; **where they differ, the official plugin wins** |
| `dotnet` | `dotnet/skills` | The **C# LSP server**, `csharp-refactoring`, `dotnet-breaking-changes` |
| `dotnet-data` | `dotnet/skills` | `optimizing-ef-core-queries` |
| `microsoft-docs` | `microsoftdocs/mcp` | Microsoft Learn MCP server + concept/API-reference skills |

- `dotnet-test` is not included. The kit's `testing` and `tdd` skills cover xUnit, and `dotnet-test` leans toward MSTest.
- **Prerequisite per machine:** `dotnet tool install -g CWM.RoslynNavigator`, required by the kit's Roslyn MCP. `AGENTS.md` and `rename-template` list it. The Aspire CLI is listed too.
- The builder must verify marketplace names, plugin ids and the settings keys against the current docs. Sources disagree on the `microsoft-docs` marketplace name.
- **Do not run `aspire agent init`.** It writes telemetry hooks into the user-level `~/.claude/settings.json` even with `--scope project`.
- **Do not run `/dotnet-init`.** It overwrites `CLAUDE.md`, which must stay `@AGENTS.md`.
- **Exception: shadcn is copied in.** The official shadcn skill is installed into `.claude/skills/shadcn` with `npx -y skills add shadcn/ui --skill shadcn --agent claude-code`, because it is distributed through the skills CLI. Verify against ui.shadcn.com/docs/skills first. `AGENTS.md` notes how to refresh it.

**Kit overrides.** Plugin skills can't be edited, so conflicts are settled in three places:

1. **`AGENTS.md` → "dotnet-claude-kit overrides" section**, which opens with: "dotnet-claude-kit is the base. Where it conflicts with this file, this file wins."

   | Kit says | This template does |
   |---|---|
   | Hand-rolled `Result` / `Result<T>` | **OneOf** everywhere. Never introduce a `Result` type |
   | FluentValidation via `ValidationFilter<T>` / Mediator `ValidationBehavior`, which throw `ValidationException` | FluentValidation run by the action registry with `ValidateAsync`, **never throwing**. Value objects own the rules through `MustBeValid` (§7.5) |
   | Mediator / MediatR `IRequest` + handlers; `IEndpointGroup` endpoints; `/scaffold` for features | **The action registry.** One `[Action]`/`[Query]` class generates the HTTP + MCP + preview. Never hand-write endpoints or MCP tools for actions. Use the `add-action` skill, not `/scaffold` |
   | Domain events implement Mediator `INotification` | Our own `IDomainEvent`. Domain depends only on OneOf |
   | Integration tests on Testcontainers Postgres | In-memory **SQLite** with real migrations. EF `InMemory` stays banned, as in the kit |
   | Clean Architecture in 4 projects (Domain/Application/Infrastructure/Api) | **Domain + Api** (§7.1). `/arch-check` must be judged against §7.1 |
   | Value objects validate in the constructor | Private constructor + `Create` returning OneOf |
   | Assertions with xUnit `Assert` | **Shouldly** |
   | ProblemDetails for errors | Same, plus the required extensions `code`, `details`, `suggestion` and `traceId` (§7.5) |
   | Aspire examples with `AddPostgres` / `AddNpgsqlDbContext` | `AddSqlite` for v1; Postgres only via `swap-to-postgres` |

   Where the kit agrees, `AGENTS.md` links to it instead of restating it. That covers DbContext as the unit of work with no repositories, `IEntityTypeConfiguration`, no lazy loading, `.Select()` projections, never auto-migrating in production, xUnit v3, `FakeTimeProvider`, `IMeterFactory`, `[LoggerMessage]`, low-cardinality tags and strongly typed ids.
2. **`.claude/rules/`.** The kit ships its 10 rules as files to copy. They are copied in, and `error-handling`, `architecture` and `testing` are edited to match the table above. The others are kept as shipped. Each edited rule starts with a line saying it differs from the kit, and why.
3. **Repo-owned delta skills.** `domain-modeling`, `ef-core` and `tdd` (below) each begin "Base: `dotnet-claude-kit:<skill>`. Differences in this template:" and hold only the differences plus template-specific procedures.

The architecture tests (§7.10) enforce the most important overrides mechanically: no `ValidationException`, no Mediator/MediatR, no `Result<T>` types, no EF `InMemory`.

**Repo-owned skills** go in `.claude/skills/`. Their names must not collide with kit skills; the builder checks this, and the plugin namespace covers it for the deltas.

| Skill | Purpose |
|---|---|
| `domain-modeling` | Delta on the kit's `ddd`. OneOf results, VO `Create` returning OneOf, `IAppError` design (including how to write a good `suggestion`), our `IDomainEvent`, and passing DB-dependent checks into the domain. Anti-patterns: anemic entities, public setters, logic in actions. |
| `ef-core` | Delta on the kit's `ef-core`. Mapping VOs (converters and complex types), the `Version` concurrency token, migrations on SQLite and the Postgres-portable rule, and data-preserving changes. Links to `optimizing-ef-core-queries`. |
| `tdd` | Delta on the kit's `tdd`/`testing`. Write the `{Action}Tests` class with `[Covers]` for the happy path, each error code, `validation.failed` and the stale case, using the `Preview`/`Execute` helpers and the Shouldly extensions. Watch it fail, implement domain then validator then action, go green, then run `verify.sh`. |
| `add-action` | The end-to-end recipe, in order: VO(s) → aggregate behavior + domain errors → validator (`MustBeValid` + input rules) → action/query class → EF mapping + migration → integration test class → optional UI → `verify.sh`. It refers to `tdd`, `domain-modeling`, `ef-core` and `frontend-feature` for each step. |
| `add-aggregate` | A new aggregate root with id VO, EF configuration, migration and first action. |
| `change-action-contract` | Changing an existing action's input, output or error codes. These are MCP tool contracts that runtime agents depend on, so the skill covers when a change is additive and when it breaks something, how to add instead of rename, and which tests and descriptions to update. |
| `frontend-feature` | A UI page for an action or query: the generated client, TanStack Query, `<DataTable>`, `<PreviewConfirmDialog>`, and shadcn components added via the CLI. Links to `shadcn`. |
| `investigate` | The debugging order: error code → `traceId` → trace and logs via the Aspire MCP (`aspire-monitoring`) → the failing test's captured spans and logs. |
| `remove-example` | Deletes the Projects example cleanly (code, migrations, UI, tests) and leaves the framework green. |
| `rename-template` | Runs `scripts/rename.sh` and checks the result. |
| `swap-to-postgres` | AppHost resource, provider registration, migration regeneration, and test implications. |

- **Skill lint.** A test in `AppTemplate.Architecture.Tests` parses every repo-owned `SKILL.md` and fails if:
  - the frontmatter (`name`, `description`) is missing;
  - any repo path, script or `dotnet`/`npm run` target the skill names in backticks no longer exists;
  - a skill links to another repo-owned skill that doesn't exist.

  The test asserts it found at least the skills listed above. This keeps skills from going stale as the code moves.

#### 7.11.2 MCP servers for coding agents

- **`.mcp.json`** registers:
  - Aspire: `aspire agent mcp` (stdio);
  - shadcn: `npx shadcn@latest mcp`;
  - Context7: the remote `https://mcp.context7.com/mcp`. An API key is optional and raises rate limits; `AGENTS.md` documents setting it through an env var, never committed;
  - the app's own MCP endpoint (`http://localhost:<fixed port>/mcp`), so coding agents can test features as a runtime agent would.
- The Microsoft Learn MCP comes through the `microsoft-docs` plugin. If the plugin doesn't bundle it, the builder adds `https://learn.microsoft.com/api/mcp` to `.mcp.json` directly. It must not be registered twice.
- **Frontend rules in `AGENTS.md`:**
  - add components via `npx shadcn@latest add`;
  - never edit `components/ui/*`, compose on top instead;
  - use Tailwind tokens only, no custom CSS;
  - lists use `<DataTable>`, and mutations use `<PreviewConfirmDialog>`.
- **`scripts/verify.sh`** runs, in order: `dotnet build` (warnings as errors), `dotnet test`, OpenAPI → TS client regeneration with a drift check, and `npm run typecheck && npm run lint && npm run build`. It exits non-zero on any failure. This is the one command agents run before claiming done.

### 7.12 Example feature: Projects with Tasks

```
Project (aggregate root): ProjectId, ProjectName (VO, 1–100 chars, trimmed), Status (Active|Archived), Version
  Tasks: TaskItem (TaskItemId, TaskTitle VO 1–200 chars, Done)
Mutations: create_project, rename_project, add_task, complete_task, archive_project (destructiveHint)
Queries:   list_projects (filter by status), get_project
Errors:    project.not_found, project.name_taken, project.archived, task.not_found, task.already_done,
           validation.failed (field codes: project_name.empty / .too_long, task_title.empty / .too_long)
Events:    ProjectCreated, ProjectRenamed, TaskAdded, TaskCompleted, ProjectArchived
UI:        Projects page (DataTable + create dialog), Project detail (rename, tasks DataTable, add/complete, archive) — all via PreviewConfirmDialog
```

It covers value objects, a child collection, invariants, a DB-dependent check (uniqueness), creates (id allocation), updates (version binding) and a state transition.

### 7.13 Template distribution

My call: a **Git template repository**, cloned and then renamed with a script `scripts/rename.sh <NewName>` that replaces the `AppTemplate` prefix in file names, namespaces and the solution. A `dotnet new` template package is deferred.

## 8. Left to the builder

- Exact interface and method signatures for `IMutation`/`IQuery`/`ActionContext`, and how the registry reflects over them, while keeping the §7.3 guarantees.
- Package versions. Verify the Aspire 17 compatibility of the CommunityToolkit SQLite and Vite hosting packages, the plugin marketplace names and ids, the `.claude/settings.json` plugin keys, and the shadcn skill command.
- The wording of each repo-owned skill, within the purposes listed in §7.11.1.
- The architecture-test library (ArchUnitNET or NetArchTest).
- The FluentValidation bridge internals (`MustBeValid`, `WithSuggestion`, default field codes) within §7.5.
- Change-set normalization and hashing details, and the JSON shape of `details` per error.
- UI layout and styling within the shadcn defaults.
- Router choice and the frontend folder structure.
- Naming inside folders.

## 9. Verification of done (v1)

- `scripts/verify.sh` exits 0 on a fresh clone. A deliberately broken test makes it exit non-zero.
- `dotnet run --project src/AppTemplate.AppHost` starts the dashboard, the SQLite resource, the Api and Vite without Docker.
- Through the app's MCP endpoint, an MCP client can run `preview_create_project` → `create_project` → `list_projects`, and a stale token returns `preview.stale` with a suggestion and a `traceId`.
- The React UI lists, creates, renames and archives projects through the preview dialog.
- After `remove-example`, `verify.sh` is still green.
