using System.Diagnostics;

namespace AppTemplate.Api.Actions.Telemetry;

/// <summary>Names and the <see cref="ActivitySource"/> for action spans and metrics (spec §7.8).</summary>
public static class ActionTelemetry
{
    /// <summary>Source and meter name. ServiceDefaults repeats this string because it cannot reference the Api.</summary>
    public const string Name = "AppTemplate.Actions";

    public static readonly ActivitySource Source = new(Name);

    public const string TagName = "action.name";
    public const string TagPhase = "action.phase";
    public const string TagOutcome = "action.outcome";
    public const string TagAggregateType = "aggregate.type";
    public const string TagAggregateId = "aggregate.id";
    public const string TagStaleReason = "preview.stale_reason";

    public const string OutcomeSuccess = "success";
    public const string OutcomeUnexpected = "internal.unexpected";
}
