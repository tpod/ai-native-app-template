---
name: domain-modeling
description: Use when designing or changing value objects, aggregate behavior, domain errors or domain events in src/AppTemplate.Domain. Template-specific delta on the kit's ddd skill.
---

# Domain modeling

Base: `dotnet-claude-kit:ddd`. Differences in this template:

- **OneOf, never `Result`.** Domain methods return `OneOf<Success, E1, …>` or `OneOf<T, E1, …>`. Never define or use a `Result`/`Result<T>` type, including `OneOf.Types.Result`. `NoResultType_IsDefinedOrUsed` fails the build if you do.
- **Value objects validate in `Create`, not in a constructor.** Private constructor, `static Create(...)` returning `OneOf<TVo, ValidationError>`.
- **Errors are `AppError` records with `[Error]`**, not exceptions and not strings.
- **Events implement our `IDomainEvent`**, not Mediator `INotification`. The Domain project references only OneOf (`Domain_References_OnlyOneOf`).
- **No repositories, no domain services that query.** Actions load data and pass it in.

Building blocks live in `src/AppTemplate.Domain/Common/`: `AggregateRoot<TId>`, `Entity<TId>`, `IValueObject<TSelf, TValue>`, `AppError`, `ErrorAttribute`, `ErrorKind`, `ValidationError`, `ValidationFailed`, `IDomainEvent`.

## Value objects

Copy `src/AppTemplate.Domain/Projects/ProjectName.cs`:

```csharp
public sealed record ProjectName : IValueObject<ProjectName, string>
{
    public const int MaxLength = 100;
    private ProjectName(string value) => Value = value;
    public string Value { get; }

    public static OneOf<ProjectName, ValidationError> Create(string? raw)
    {
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return new ValidationError("project_name.empty", "Project name is empty.", "Provide a non-blank project name.");
        …
        return new ProjectName(trimmed);
    }

    public static ProjectName FromTrusted(string value) => new(value); // persistence only, skips validation
}
```

- `sealed`, no public constructor with parameters. `ValueObjects_AreSealedWithoutPublicParameterizedConstructors` enforces it.
- Normalize in `Create` (trim, case rules) so equality is meaningful.
- `ValidationError(Code, Message, Suggestion)`: the code is `<vo_snake>.<problem>` and matches `^[a-z_]+\.[a-z_]+$`.
- Expose limits as constants (`MaxLength`). EF mapping and validators reuse them.
- Implementing `IValueObject<TSelf, TValue>` is what lets `ValueObjectConventions` map the VO to one column automatically (`skill:ef-core`). The validator reuses `Create` through `MustBeValid` (`skill:add-action`).
- Ids are `readonly record struct` VOs with `From` and `FromTrusted`. See `src/AppTemplate.Domain/Projects/ProjectId.cs`.
- Multi-value VOs (an address, a money amount) are `sealed record`s with `Create` too, but they do not implement `IValueObject`. Map them as EF complex types (`skill:ef-core`).

## Aggregates and entities

Copy `src/AppTemplate.Domain/Projects/Project.cs`:

- Derive from `AggregateRoot<TId>`. It provides `Id`, `Version`, `DomainEvents` and `Raise`. Never touch `Version`; the pipeline increments it on execute when something changed.
- Properties use `{ get; private set; }`. `DomainEntities_HaveNoPublicSetters` rejects public setters and init accessors.
- A private parameterless constructor for EF (`private Project() => Name = null!;`), plus a private constructor used by the static factory.
- Creation is a static factory returning OneOf: `Project.Create(ProjectId id, ProjectName name, ProjectId? currentHolderOfName)`.
- Child collections: a private `List<T>` field exposed as `IReadOnlyList<T>`. Child entities (`src/AppTemplate.Domain/Projects/TaskItem.cs`) have `internal` constructors and mutators, so only the aggregate changes them.
- Each behavior method checks invariants in order and returns the first error, for example archived, then not found, then already done (`Project.CompleteTask`).
- A no-op returns `Success` without changing state or raising an event.

### DB-dependent checks are parameters

The domain never queries. The action loads what the rule needs and passes it in:

```csharp
// Action (Api): src/AppTemplate.Api/Features/Projects/RenameProject.cs
var holder = await ProjectLookup.FindNameHolder(name, ctx);
return target.Rename(name, holder) …

// Domain: the rule itself
if (currentHolderOfName is { } holder && holder != Id) return new ProjectNameTaken(name.Value, holder);
```

Name the parameter after what it means (`currentHolderOfName`), not how it was fetched.

## Domain errors

In `src/AppTemplate.Domain/<Aggregate>/<Aggregate>Errors.cs`, like `src/AppTemplate.Domain/Projects/ProjectErrors.cs`:

```csharp
[Error("project.name_taken", ErrorKind.Conflict, "Project name already taken")]
public sealed record ProjectNameTaken(string Name, ProjectId ExistingProjectId) : AppError
{
    public override string Message => $"An active project named '{Name}' already exists.";
    public override string Suggestion => "Choose a different name; list_projects shows existing names.";
    public override object? Details => new { name = Name, existingProjectId = ExistingProjectId.Value };
}
```

- Code `area.snake_case`, unique across the solution (`ErrorCodes_AreUniqueAndWellFormed`). The title is stable; never put data in it.
- `ErrorKind` sets the HTTP status: Validation 422, NotFound 404, Conflict 409 (state clash), Precondition 409 (a precondition the caller must refresh), BadRequest 400, Unexpected 500.
- `Message` is for humans and may include values. `Details` is a small camelCase object an agent can act on: ids, the conflicting value. Never put secrets or whole entities in it.
- **A good `Suggestion`** is the next step the caller can take, names the tool that helps, and never restates the error:
  - Good: "Choose a different name; list_projects shows existing names."
  - Good: "Check the ids; get_project lists the project's tasks."
  - Bad: "The project name is already taken." (restates the error)
  - Bad: "Try again." (retrying cannot help a conflict)
- Do not create an error for something the validator already guarantees. Do create one for every rule the aggregate enforces.

## Domain events

In `src/AppTemplate.Domain/<Aggregate>/<Aggregate>Events.cs`: past-tense `sealed record`s implementing `IDomainEvent`, carrying primitive ids and values (`ProjectRenamed(Guid ProjectId, string OldName, string NewName)`).

- Raise them inside the behavior method, only when state changed.
- They appear in the preview as `sideEffects`.
- Handlers implement `IDomainEventHandler<TEvent>` (`src/AppTemplate.Api/Actions/Events/IDomainEventHandler.cs`) in the Api. Register one per concrete event type in `src/AppTemplate.Api/Program.cs`, for example `builder.Services.AddScoped<IDomainEventHandler<ProjectRenamed>, NotifyOnRename>();`.
- Handlers run after the commit, in the same scope, best-effort and in-process (no outbox). A handler failure returns 500 but does not undo the change.

## Anti-patterns

| Don't | Do |
|---|---|
| Anemic entity with public setters; the action sets fields | A behavior method on the aggregate that enforces the rule |
| Business rules in the action's `Apply` (`if (target.Status == Archived) return …`) | Rules in the domain method; the action only converts input, loads data and calls it |
| The domain calling the database or a service | The action loads the data and passes it in as a parameter |
| `throw new InvalidOperationException` for a rule violation | Return an `AppError` record through OneOf |
| Public VO constructor or validation in the constructor | Private constructor + `Create` returning OneOf |
| One generic `DomainError(string code)` | One record per code with typed `Details` |
| Calling `IncrementVersion` or `SaveChanges` from the domain | Leave both to the pipeline |
