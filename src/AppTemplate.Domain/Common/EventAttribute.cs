namespace AppTemplate.Domain.Common;

/// <summary>
/// The stable name and payload version of a domain event. The name is the history contract: it is stored with every
/// history row, so never rename it. Bump <see cref="Version"/> when the payload shape changes incompatibly.
/// </summary>
/// <param name="name">Lowercase dotted, <c>&lt;aggregate&gt;.&lt;past_tense_verb&gt;</c>, e.g. <c>project.renamed</c>.</param>
/// <param name="version">Payload version, starting at 1.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EventAttribute(string name, int version) : Attribute
{
    public string Name { get; } = name;
    public int Version { get; } = version;
}
