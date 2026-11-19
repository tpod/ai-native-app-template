using AppTemplate.Api.Actions.Preview;

namespace AppTemplate.Api.Actions;

/// <summary>The body of a successful preview. Nothing has been saved; <see cref="PreviewToken"/> authorizes the execute.</summary>
public sealed record PreviewResponse(
    string Action,
    string Summary,
    IReadOnlyList<Change> Changes,
    IReadOnlyList<SideEffect> SideEffects,
    IReadOnlyList<PreviewWarning> Warnings,
    string PreviewToken,
    DateTimeOffset ExpiresAt);

/// <summary>The body of a successful execute.</summary>
/// <param name="Result">Whatever the handler's <c>ToResult</c> returned for the saved aggregate.</param>
/// <param name="Changes">The change set actually committed; empty when the action changed nothing.</param>
public sealed record ExecuteResponse(string Action, object Result, IReadOnlyList<Change> Changes);
