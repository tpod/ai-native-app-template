namespace AppTemplate.Api.Actions;

/// <summary>Thrown at startup when one or more <c>[Action]</c>/<c>[Query]</c> types break the registry rules.</summary>
public sealed class ActionRegistrationException(IReadOnlyList<string> violations)
    : Exception(FormatMessage(violations))
{
    /// <summary>One message per violation, each naming the type and the rule.</summary>
    public IReadOnlyList<string> Violations { get; } = violations;

    private static string FormatMessage(IReadOnlyList<string> violations)
        => $"Action registration failed with {violations.Count} violation(s):{Environment.NewLine}"
            + string.Join(Environment.NewLine, violations.Select(v => $"  - {v}"));
}
