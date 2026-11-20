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

## How a mutation runs

1. **Preview.** The pipeline loads the aggregate, runs the real domain method on it and reads the diff from EF Core's change tracker. It then throws everything away. The caller gets the field changes, the domain events and a signed token.
2. **Confirm.** The agent or the UI shows that preview to a human.
3. **Execute.** The pipeline runs the same method again and commits only if it produces exactly the previewed change set. If the aggregate moved on in between, the execute is rejected as `preview.stale` and the caller previews again.

No action has preview code of its own, so the preview can never drift from the real behavior. Running the method in a transaction and rolling back was rejected: SQLite's single writer would make every preview block writes.

## Design decisions

**Domain-driven design, kept small.**
- Aggregates are the consistency boundary. Child entities are reached only through their root, and each root carries a `Version` used for optimistic concurrency.
- Behavior lives on the aggregate, with no public setters. Actions only load data and call domain methods. A check that needs the database, such as name uniqueness, is loaded by the action and passed in as data, so the domain stays pure and easy to test.
- Value objects own their rules: a private constructor plus `Create` returning `OneOf<Vo, ValidationError>`. FluentValidation calls `Create` and never throws.
- Ids are strongly typed (`ProjectId`, not `Guid`).
- Domain methods return `OneOf` unions of the precise errors, never exceptions or a `Result` type. Those error types become the action's declared error codes.

**Domain events.** Aggregates raise events (`ProjectRenamed`) that describe what happened in business terms. The preview shows them as side effects. After the commit they are dispatched in-process. There is deliberately no outbox.

**Business history (planned).** Every committed event will also be stored, in the same transaction, as an append-only history row. Each row records the action, the channel, the trace id and an optional `reason` from the caller. One `get_history` tool then lets an agent see what happened to an aggregate and why, without reconstructing it from current state. To make that history complete, every state change must go through an action and must raise an event.

**Entity Framework Core.**
- `DbContext` is the unit of work. There are no repositories.
- The change tracker does double duty as the preview engine.
- Value objects are mapped by convention, without per-property configuration.
- Provider-specific details stay in `Data/`, so the move to Postgres is a recipe, not a rewrite.
- Migrations apply automatically only in development.

**Two projects: Domain + Api.** Domain references only OneOf. Clean Architecture's four layers add ceremony without value here.

**Errors agents can act on.** Every error is RFC 9457 ProblemDetails with a stable `code`, a `suggestion` that names the next step (often a tool), and a `traceId`. HTTP and MCP return the same JSON.

**Tests as the guardrail.** Integration tests run on in-memory SQLite with the real migrations: no Docker and no EF InMemory. Architecture tests fail the build on:
- a layering violation or a banned type
- a malformed error code
- an action without tests for its happy path, each of its error codes and the stale case

**Auth deferred.** v1 runs on localhost behind an origin check. Add auth before exposing it.

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
