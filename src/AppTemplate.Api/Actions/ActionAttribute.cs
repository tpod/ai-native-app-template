namespace AppTemplate.Api.Actions;

/// <summary>
/// Marks a class as a mutation handler. The class must implement <see cref="IMutation{TInput, TAggregate}"/>.
/// It is served as preview + execute HTTP endpoints and MCP tools.
/// </summary>
/// <param name="name">Unique snake_case name matching <c>^[a-z][a-z0-9_]*$</c>.</param>
/// <param name="description">Non-empty description shown to humans and agents.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ActionAttribute(string name, string description) : Attribute
{
    public string Name { get; } = name;

    public string Description { get; } = description;

    /// <summary>When true, MCP execute tools carry <c>destructiveHint</c>.</summary>
    public bool Destructive { get; init; }

    /// <summary>How long a preview token stays valid.</summary>
    public int PreviewLifetimeSeconds { get; init; } = 300;
}
