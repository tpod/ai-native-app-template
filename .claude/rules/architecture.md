---
alwaysApply: true
description: >
  Enforces this template's fixed Domain + Api architecture, the action
  registry instead of hand-written endpoints, dependency direction and
  feature organization.
---

> Edited for this template: differs from dotnet-claude-kit on choosing an architecture (it is fixed: Domain + Api), endpoint organization (the action registry generates HTTP and MCP; no `IEndpointGroup`) and the project layers. See AGENTS.md → dotnet-claude-kit overrides.
> Source: [dotnet-claude-kit](https://github.com/codewithmukesh/dotnet-claude-kit) `.claude/rules/architecture.md`, commit 6de62a5 (kit v0.14.0). MIT License, Copyright (c) 2025 Mukesh Murugan.

# Architecture Rules

## The Architecture Is Decided

- **Don't re-choose the architecture.** This template is two projects with logic: `AppTemplate.Domain` (aggregates, value objects, errors, events) and `AppTemplate.Api` (actions, data, MCP, HTTP). AGENTS.md has the map. Judge `/arch-check` and architecture advice against that map, not against Clean Architecture's four projects.

## Data Access

- **No repository pattern over EF Core.** `DbContext` is already a Unit of Work + Repository. Wrapping it adds indirection with no value and prevents access to EF Core features like change tracking, batching, and compiled queries. Actions reach the database through `ctx.Db` (`ActionContext`).

```csharp
// DO: query the DbContext directly (src/AppTemplate.Api/Features/Projects/ProjectLookup.cs)
return ctx.Db.Projects.Include(p => p.Tasks).FirstOrDefaultAsync(p => p.Id == projectId, ctx.CancellationToken);

// DON'T: generic repository wrapping EF
public interface IRepository<T> { Task<T?> GetByIdAsync(Guid id); }
```

- **No provider-specific SQL in domain or feature code.** No raw SQL, no SQLite-only functions. Provider details stay in `src/AppTemplate.Api/Data` so the Postgres swap stays a configuration change.

## Endpoints and MCP Tools Are Generated

- **Never hand-write endpoints or MCP tools for actions.** One class marked `[Action("name", "description")]` (implements `IMutation<TInput, TAggregate>`) or `[Query("name", "description")]` (implements `IQuery<TInput, TOutput>`) in `Features/<Aggregate>/` is the whole feature surface. The action registry generates `POST /api/actions/{name}/preview`, `POST /api/actions/{name}`, `POST /api/queries/{name}`, the MCP tools and the OpenAPI operations from it.
- **Program.cs calls `app.MapActions()` once.** Adding an action never changes Program.cs. Don't add `IEndpointGroup`, `MapGroup`, `Map*Endpoints()`, controllers or `[McpServerTool]` classes.

```csharp
// DO: one action class; HTTP, MCP and preview come from the registry
[Action("rename_project", "Rename an active project. …")]
public sealed class RenameProject : IMutation<RenameProjectInput, Project> { /* Load, Apply, Summarize, ToResult */ }

// DON'T: hand-written endpoint or MCP tool
app.MapPost("/api/projects/{id}/rename", RenameProjectHandler);
[McpServerTool] public static Task<string> RenameProject(...) => ...;
```

## Project Organization

- **Feature folders over layer folders.** Vertical slices keep related code together, reducing the number of files you touch per feature.

```
src/AppTemplate.Api/
  Actions/        # framework: registry, preview engine, token, validation bridge, ProblemDetails
  Features/
    Projects/     # one file per action or query, plus validators and DTOs
      RenameProject.cs
      ListProjects.cs
      ProjectValidators.cs
  Data/           # AppDbContext, EF configurations, Migrations/
  Mcp/            # MCP wiring; tools come from the registry
```

- **Dependency direction is inward.** `AppTemplate.Domain` depends only on OneOf: no EF Core, ASP.NET Core or Api references (`Domain_References_OnlyOneOf` enforces it). `AppTemplate.Api` depends on Domain. Never reverse this arrow.
- **Behavior lives on the aggregate.** Actions load data and call domain methods; the decision stays in the domain. Checks that need the database (such as name uniqueness) are loaded by the action and passed into the domain method as data.
- **Module boundaries enforced through project references.** If two modules should not couple, they must not have a project reference. Use integration events or a shared contracts project for cross-module communication.

## Shared Kernel

- **Shared kernel contains only contracts, never business logic.** In this template, `src/AppTemplate.Domain/Common` holds the base types (`AggregateRoot`, `AppError`, `IValueObject`, `IDomainEvent`). Domain logic belongs in the owning aggregate. Leaking logic into shared code creates hidden coupling.

```csharp
// DO: shared kernel has contracts
public interface IDomainEvent;

// DON'T: shared kernel has domain logic
public static class PricingCalculator { /* business rules */ }
```
