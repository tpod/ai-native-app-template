namespace AppTemplate.Api.Actions;

/// <summary>
/// Marks a class as a read-only query handler. The class must implement <see cref="IQuery{TInput, TOutput}"/>.
/// </summary>
/// <param name="name">Unique snake_case name matching <c>^[a-z][a-z0-9_]*$</c>.</param>
/// <param name="description">Non-empty description shown to humans and agents.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class QueryAttribute(string name, string description) : Attribute
{
    public string Name { get; } = name;

    public string Description { get; } = description;
}
