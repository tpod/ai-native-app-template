<img src="assets/icon.svg" width="64" height="64" alt="">

# ai-native-app-template

A full-stack .NET 10 + React template for apps that AI agents both **build** and **use**.

- **Coding agents** add features. The conventions are strict and enforced by tests, so an agent can follow them without guessing. [`AGENTS.md`](AGENTS.md) is the manual.
- **Runtime agents** operate the running app through its MCP server. Every feature is an MCP tool for them as well as an HTTP endpoint for the web UI.

## The core idea

You define a feature once, as one class:

```csharp
[Action("rename_project", "Rename an active project.")]
public sealed class RenameProject : IMutation<RenameProjectInput, Project> { /* Load, Apply */ }
```

The action registry turns that class into HTTP endpoints, an MCP tool, an OpenAPI operation and a typed TypeScript client. Nobody hand-writes endpoints or MCP tools.

## Opinionated decisions

| Decision | Why |
|---|---|
| **Preview → confirm → execute for every mutation** | An agent shows the user exactly what will change before anything is saved. The signed preview token binds the input, the aggregate versions and the change set, so a stale or altered execute is rejected. |
| **Errors are ProblemDetails with `code` and `suggestion`** | Agents act on a stable code and a next step, never on prose. HTTP and MCP return the same JSON. |
| **OneOf, never exceptions or a `Result` type** | Every failure is visible in the type, and the error type arguments become the action's declared error codes. |
| **Value objects own their rules** | `Create` returns `OneOf<Vo, ValidationError>`; FluentValidation calls it and never throws. |
| **Behavior lives on the aggregate** | No public setters. Actions load data and call domain methods, which raise domain events. |
| **Two projects: Domain + Api** | Domain references only OneOf. Clean Architecture's four layers add ceremony without value here. |
| **No repositories, no Mediator** | `DbContext` is the unit of work; the action registry replaces request handlers. |
| **Integration tests on in-memory SQLite with real migrations** | No Docker, no EF InMemory. Every action needs a test for its happy path, each error code and the stale case. |
| **Architecture tests enforce the rules** | Layering, banned types, error-code format and test coverage fail the build, not a code review. |
| **SQLite first, Postgres by recipe** | Zero setup locally; provider details stay in `Data/`. |
| **Auth deferred** | v1 runs on localhost behind an origin check. Add auth before exposing it. |

## Quick start

```bash
dotnet run --project src/AppTemplate.AppHost   # Aspire: API, MCP, web UI, dashboard
scripts/verify.sh                              # build, tests, client drift, web checks; must print VERIFY OK
```

The API is at `http://localhost:5180`, MCP at `/mcp` and OpenAPI at `/openapi/v1.json`.

## Stack

.NET 10, ASP.NET Core, EF Core, Aspire, the MCP C# SDK, FluentValidation, xUnit v3 and Shouldly. React, Vite, TypeScript, Tailwind v4, shadcn/ui and TanStack Query/Table.

## Make it yours

Coding agents get step-by-step recipes as skills in `.claude/skills/`:

- `rename-template`: rename the template to your product.
- `remove-example`: delete the Projects example.
- `add-aggregate`, `add-action`: build your own domain.
