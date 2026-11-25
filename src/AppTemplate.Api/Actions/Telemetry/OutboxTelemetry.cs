namespace AppTemplate.Api.Actions.Telemetry;

/// <summary>
/// Span and tag names for outbox deliveries. Spans use <see cref="ActionTelemetry.Source"/>, so ServiceDefaults
/// already exports them. One span per attempt, named <c>outbox deliver {subscriber}</c>, linked to the trace of the
/// execute that raised the event.
/// </summary>
public static class OutboxTelemetry
{
    public const string SpanPrefix = "outbox deliver ";

    public const string TagSubscriber = "outbox.subscriber";
    public const string TagAttempt = "outbox.attempt";
    public const string TagOutcome = "outbox.outcome";
    public const string TagEventName = "event.name";

    public const string OutcomeDone = "done";
    public const string OutcomeRetry = "retry";
    public const string OutcomeDead = "dead";
}
