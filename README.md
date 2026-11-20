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

The action registry turns that class into HTTP endpoints, an MCP tool, an OpenAPI operation and a typed TypeScript client. Nobody hand-writes endpoints or MCP tools, so no feature can exist for the UI but not for agents.

## Opinionated decisions

| Decision | Why |
|---|---|
| **Preview → confirm → execute for every mutation** | An agent shows a human exactly what will change before anything is saved. The signed token binds the input, the aggregate versions and the change set, so execute commits only what was previewed, or fails with `preview.stale`. |
| **The preview runs the real domain method and reads EF's change tracker, then discards it** | No action has preview code of its own, so the preview can't drift from the real behavior. Rolling back a transaction was rejected because SQLite's single writer would make every preview block writes. |
| **Aggregates are the consistency boundary, with a `Version` concurrency token** | Child entities change only through their root, and concurrent writes surface as stale previews, not lost updates. |
| **Behavior lives on the aggregate; actions only load data** | No public setters. A database-dependent check, such as name uniqueness, is loaded by the action and passed in as data, so the domain stays pure and easy to test. |
| **Value objects own their rules; strongly typed ids** | `Create` returns `OneOf<Vo, ValidationError>`, so an invalid value can't exist. FluentValidation calls `Create` and never throws. |
| **OneOf, never exceptions or a `Result` type** | Every failure is visible in the type, and the error type arguments become the action's declared error codes. |
| **Domain events describe business facts; no outbox** | `ProjectRenamed`, not "row updated". Events appear in the preview and are dispatched in-process after commit, which keeps v1 simple. |
| **Business history in the same transaction (planned)** | Every committed event becomes an append-only row with the action, the channel, the trace id and an optional caller `reason`. Through `get_history`, an agent taking over work, or one debugging, can see what happened and why without reconstructing it. |
| **Every state change goes through an action and raises an event (planned)** | That keeps history, validation, stale checks and telemetry complete. Jobs and imports call actions too. |
| **Errors are ProblemDetails with `code` and `suggestion`** | Agents act on a stable code and a next step, never on prose. HTTP and MCP return the same JSON, with a `traceId`. |
| **EF Core `DbContext` is the unit of work; no repositories, no Mediator** | Repositories hide EF features and add nothing. The action registry replaces request handlers. |
| **Provider details stay in `Data/`; SQLite first** | Zero setup locally, and moving to Postgres is a recipe, not a rewrite. Migrations apply automatically only in development. |
| **Two projects: Domain + Api** | Domain references only OneOf. Clean Architecture's four layers add ceremony without value here. |
| **Integration tests on in-memory SQLite with real migrations** | No Docker and no EF InMemory. Every action needs tests for its happy path, each of its error codes and the stale case. |
| **Architecture tests enforce the rules** | Layering, banned types, error-code format and test coverage fail the build, not a code review. |
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
- `swap-to-postgres`: move off SQLite.
