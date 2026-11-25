namespace AppTemplate.Api.Data;

/// <summary>
/// One event to deliver to one subscriber. Written by the action pipeline in the same save as the aggregate change
/// and its history row; afterwards only <c>OutboxStore</c> changes it. The event itself (name, payload,
/// aggregate, trace id) lives on <see cref="HistoryEntry"/>; nothing is copied except the ordering key.
/// </summary>
public sealed class OutboxDelivery
{
    public const int LastErrorMaxLength = 2000;

    // EF only.
    private OutboxDelivery()
    {
        HistoryEntry = null!;
        Subscriber = null!;
        AggregateType = null!;
        AggregateId = null!;
    }

    public OutboxDelivery(HistoryEntry entry, string subscriber, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);
        HistoryEntry = entry;
        Subscriber = subscriber;
        AggregateType = entry.AggregateType;
        AggregateId = entry.AggregateId;
        Status = OutboxStatus.Pending;
        NextAttemptAt = now;
    }

    /// <summary>Store-generated and monotonic; also the id receivers dedupe on.</summary>
    public long Id { get; init; }

    public long HistoryEntryId { get; init; }

    public HistoryEntry HistoryEntry { get; init; }

    /// <summary><c>handler:&lt;name&gt;</c>, <c>publisher:&lt;name&gt;</c> or (phase 3) <c>webhook:&lt;id&gt;</c>.</summary>
    public string Subscriber { get; init; }

    /// <summary>With <see cref="AggregateId"/> and <see cref="Subscriber"/>, the per-aggregate ordering key.</summary>
    public string AggregateType { get; init; }

    public string AggregateId { get; init; }

    public OutboxStatus Status { get; init; }

    public int Attempts { get; init; }

    public DateTimeOffset NextAttemptAt { get; init; }

    public DateTimeOffset? LeaseUntil { get; init; }

    public string? LastError { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}
