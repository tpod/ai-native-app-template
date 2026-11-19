using AppTemplate.Domain.Common;
using OneOf;

namespace AppTemplate.Api.Actions;

public enum ActionKind { Mutation, Query }

/// <summary>
/// Everything the HTTP, MCP and OpenAPI layers need to serve one <c>[Action]</c> or <c>[Query]</c>,
/// built and validated by <see cref="ActionRegistry"/>.
/// </summary>
/// <param name="Name">The snake_case action name.</param>
/// <param name="Description">The human and agent description.</param>
/// <param name="Kind">Mutation or query.</param>
/// <param name="HandlerType">The handler class, resolved from DI per call.</param>
/// <param name="InputType">The input record.</param>
/// <param name="AggregateType">The aggregate a mutation changes; null for queries.</param>
/// <param name="OutputType">The query's success type; null for mutations, whose result comes from <see cref="ToResult"/>.</param>
/// <param name="ErrorTypes">Distinct non-T0 OneOf arguments of Load + Apply (mutations) or Run (queries), in declaration order.</param>
/// <param name="HasValidator">True when an <c>IValidator&lt;TInput&gt;</c> is registered.</param>
/// <param name="Destructive">From <see cref="ActionAttribute.Destructive"/>; false for queries.</param>
/// <param name="PreviewLifetime">How long a preview token is valid; <see cref="TimeSpan.Zero"/> for queries.</param>
public sealed record ActionDescriptor(
    string Name,
    string Description,
    ActionKind Kind,
    Type HandlerType,
    Type InputType,
    Type? AggregateType,
    Type? OutputType,
    IReadOnlyList<Type> ErrorTypes,
    bool HasValidator,
    bool Destructive,
    TimeSpan PreviewLifetime)
{
    /// <summary>
    /// Every error code the action can return: the codes of <see cref="ErrorTypes"/>, plus <c>validation.failed</c>
    /// when <see cref="HasValidator"/>. Distinct and sorted ordinally. Framework errors such as <c>preview.stale</c> are not included.
    /// </summary>
    public IReadOnlyList<string> ErrorCodes =>
    [
        .. ErrorTypes.Select(t => ErrorMetadata.For(t).Code)
            .Concat(HasValidator ? [ErrorMetadata.For(typeof(ValidationFailed)).Code] : [])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    // Compiled invokers. Arguments are (handler, ...) where handler is an instance of HandlerType resolved from DI,
    // and every other object argument must be of the declared runtime type (InputType, AggregateType).
    // The Task-returning invokers await the convention method and box its OneOf result as IOneOf.
    // Invokers for the other kind are null.

    /// <summary>Mutations: calls <c>Load(input, ctx)</c>. T0 of the result is the aggregate.</summary>
    internal Func<object, object, ActionContext, Task<IOneOf>>? LoadAsync { get; init; }

    /// <summary>Mutations: calls <c>Apply(target, input, ctx)</c>. T0 of the result is <c>Success</c>.</summary>
    internal Func<object, object, object, ActionContext, Task<IOneOf>>? ApplyAsync { get; init; }

    /// <summary>Queries: calls <c>Run(input, ctx)</c>. T0 of the result is the output.</summary>
    internal Func<object, object, ActionContext, Task<IOneOf>>? RunAsync { get; init; }

    /// <summary>Mutations: calls <see cref="IMutation{TInput, TAggregate}.Summarize"/> as (handler, target, input).</summary>
    internal Func<object, object, object, string>? Summarize { get; init; }

    /// <summary>Mutations: calls <see cref="IMutation{TInput, TAggregate}.ToResult"/> as (handler, target).</summary>
    internal Func<object, object, object>? ToResult { get; init; }
}
