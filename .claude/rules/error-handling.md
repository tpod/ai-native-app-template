---
alwaysApply: true
description: >
  Enforces OneOf results for expected failures, ProblemDetails with code,
  details, suggestion and traceId for every error, non-throwing validation,
  and boundary-only exception handling in this template.
---

> Edited for this template: differs from dotnet-claude-kit on Result types (OneOf instead), the required ProblemDetails extensions, the exception handler (already wired) and validation (never throws). See AGENTS.md → dotnet-claude-kit overrides.
> Source: [dotnet-claude-kit](https://github.com/codewithmukesh/dotnet-claude-kit) `.claude/rules/error-handling.md`, commit 6de62a5 (kit v0.14.0). MIT License, Copyright (c) 2025 Mukesh Murugan.

# Error Handling Rules

## OneOf Results Over Exceptions

- **DO** return `OneOf<T, E1, E2, …>` for expected failure paths (not found, validation, conflict). Domain methods return precise unions, e.g. `OneOf<Success, ProjectIsArchived, ProjectNameTaken>`.
  Rationale: Exceptions hide control flow. OneOf makes every failure explicit in the type system, and the action registry reads the error type arguments as the action's declared error codes.

- **DON'T** define or use a `Result` / `Result<T>` type, including `OneOf.Types.Result`.
  Rationale: One result style everywhere. `NoResultType_IsDefinedOrUsed` in `tests/AppTemplate.Architecture.Tests` fails the build otherwise.

- **DON'T** use try-catch for flow control. If you can predict the failure, return it as an error type.
  Rationale: try-catch obscures the happy path and makes code harder to reason about.

- **DO** model each error as a `sealed record` deriving from `AppError`, with `[Error("<area>.<snake_case>", ErrorKind.X, "Stable title")]`, and override `Message`, `Suggestion` and, when useful, `Details`.
  Rationale: `ErrorKind` (`Validation`, `NotFound`, `Conflict`, `Precondition`, `BadRequest`, `Forbidden`, `Unexpected`) maps to the HTTP status in one place, and the code is the stable contract agents act on.

```csharp
// DO: a domain error (src/AppTemplate.Domain/Projects/ProjectErrors.cs)
[Error("project.name_taken", ErrorKind.Conflict, "Project name already taken")]
public sealed record ProjectNameTaken(string Name, ProjectId ExistingProjectId) : AppError
{
    public override string Message => $"An active project named '{Name}' already exists.";
    public override string Suggestion => "Choose a different name; list_projects shows existing names.";
    public override object? Details => new { name = Name, existingProjectId = ExistingProjectId.Value };
}

// DON'T
public sealed record Result<T>(T? Value, string? Error);
```

- **DO** write the `Suggestion` as the next step an agent can take, naming a tool when one helps. Codes match `^[a-z_]+\.[a-z_]+$` and are unique; `ErrorCodes_AreUniqueAndWellFormed` enforces both.

## ProblemDetails for HTTP Responses

- **DO** return ProblemDetails (RFC 9457) for all error responses, with the extensions `code`, `details`, `suggestion` and `traceId` always present. MCP returns the same JSON with `isError: true`.
  Rationale: Agents act on `code` and `suggestion` and quote `traceId`; they never parse `detail`.

- **DON'T** build ProblemDetails by hand, return bare strings or ad-hoc JSON. Return the `AppError` from the action; the framework (`AppErrorProblemDetails` in `src/AppTemplate.Api/Actions/Http`) writes the response.
  Rationale: One mapping keeps the shape identical on HTTP, MCP and framework-generated errors (404, 415, malformed JSON).

## Exception Handling Boundaries

- **DON'T** add another `UseExceptionHandler()` or `IExceptionHandler`. `UseAppProblemDetails()` in `src/AppTemplate.Api/Program.cs` already wires the trace header, the exception handler and status-code pages. Unhandled exceptions become `internal.unexpected` with a `traceId`; the exception goes to logs and traces only.
  Rationale: A second handler would bypass the required extensions or leak exception details.

- **DON'T** catch bare `Exception` unless at the application boundary (middleware/top-level handler).
  Rationale: Broad catches swallow bugs silently. Only the outermost layer should catch everything.

- **DON'T** catch and rethrow without adding context. Either handle it or let it propagate.
  Rationale: Catch-and-rethrow without value destroys stack traces and adds noise.

## Boundary Validation

- **DO** validate at system boundaries: action input, external service responses, file/config data.
  Rationale: Bad data should be rejected at the edge before it corrupts internal state.

- **DO** validate action input with a FluentValidation `AbstractValidator<TInput>` in the Api. The action registry runs it with `ValidateAsync` and returns all failures as one `validation.failed` error. Value objects own their rules: use `RuleFor(x => x.Name).MustBeValid(ProjectName.Create)`, and add `.WithSuggestion("…")` and `.WithErrorCode("…")` to plain rules.
  Rationale: The value object's `Create` stays the single source of each rule, and every field failure carries a code and a suggestion.

- **DON'T** call `ValidateAndThrow`, throw or catch `ValidationException`, or add a `ValidationFilter<T>` or Mediator `ValidationBehavior`.
  Rationale: Validation never throws in this template. `Api_DoesNotUseBannedTypes` bans `ValidationException`.

- **DON'T** defensively validate inside internal/private methods.
  Rationale: Internal code should trust validated data. Double-validation adds noise without safety. Domain code still builds value objects through `Create`, never from raw primitives.
