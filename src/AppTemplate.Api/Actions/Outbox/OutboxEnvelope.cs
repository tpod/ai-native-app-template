using System.Text.Json;

namespace AppTemplate.Api.Actions.Outbox;

/// <summary>
/// One committed event as external subscribers see it (publishers now, webhooks in phase 3). <see cref="Data"/> is the
/// payload exactly as history stored it, never re-serialized from a CLR type, so an event version bump does not
/// break pending deliveries.
/// </summary>
/// <param name="Id">The delivery id: stable across retries of this event to this subscriber; dedupe on it.</param>
public sealed record OutboxEnvelope(
    long Id,
    string Event,
    int EventVersion,
    string AggregateType,
    string AggregateId,
    int AggregateVersion,
    DateTimeOffset OccurredAt,
    string? TraceId,
    JsonElement Data);
