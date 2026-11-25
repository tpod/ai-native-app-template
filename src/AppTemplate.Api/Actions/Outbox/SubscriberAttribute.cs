namespace AppTemplate.Api.Actions.Outbox;

/// <summary>
/// The stable name of an outbox subscriber (an event handler or publisher). It is stored on every delivery row as
/// <c>handler:&lt;name&gt;</c> or <c>publisher:&lt;name&gt;</c>, so renaming the class never orphans pending rows.
/// Never rename it while rows are pending. Names match <c>^[a-z0-9-]+$</c> and are unique.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SubscriberAttribute(string name) : Attribute
{
    public string Name { get; } = name;

    /// <summary>Publishers only: the <c>[Event]</c> names to receive. Null receives every event.</summary>
    public string[]? Events { get; init; }
}
