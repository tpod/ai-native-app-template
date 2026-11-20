namespace AppTemplate.Api.Data;

/// <summary>
/// One committed domain event, stored append-only in the same transaction as the aggregate change it describes.
/// Written only by the action pipeline; never updated or deleted. Read through the <c>get_history</c> query.
/// </summary>
public sealed class HistoryEntry
{
    public const int ReasonMaxLength = 500;

    // EF only.
    private HistoryEntry()
    {
        AggregateType = null!;
        AggregateId = null!;
        EventType = null!;
        Payload = null!;
        Action = null!;
        Channel = null!;
    }

    public HistoryEntry(
        string aggregateType,
        string aggregateId,
        int aggregateVersion,
        string eventType,
        int eventVersion,
        string payload,
        DateTimeOffset occurredAt,
        Guid executionId,
        string action,
        string channel,
        string? client,
        string? reason,
        string? traceId)
    {
        AggregateType = aggregateType;
        AggregateId = aggregateId;
        AggregateVersion = aggregateVersion;
        EventType = eventType;
        EventVersion = eventVersion;
        Payload = payload;
        OccurredAt = occurredAt;
        ExecutionId = executionId;
        Action = action;
        Channel = channel;
        Client = client;
        Reason = reason;
        TraceId = traceId;
    }

    /// <summary>Store-generated and monotonic: the global order of history. Agents cite it as evidence.</summary>
    public long Id { get; init; }

    /// <summary>CLR name of the owning aggregate root, e.g. <c>Project</c>.</summary>
    public string AggregateType { get; init; }

    /// <summary>The owning root's key as the change set writes it (a lowercase GUID for Guid ids).</summary>
    public string AggregateId { get; init; }

    /// <summary>The owning root's <c>Version</c> after the execute that raised the event.</summary>
    public int AggregateVersion { get; init; }

    /// <summary>The event's stable <c>[Event]</c> name, e.g. <c>project.renamed</c>.</summary>
    public string EventType { get; init; }

    public int EventVersion { get; init; }

    /// <summary>The event serialized as JSON with the API's serializer options.</summary>
    public string Payload { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    /// <summary>Groups the rows written by one execute.</summary>
    public Guid ExecutionId { get; init; }

    /// <summary>The action that raised the event, e.g. <c>rename_project</c>.</summary>
    public string Action { get; init; }

    /// <summary><c>http</c>, <c>mcp</c> or <c>in_process</c>.</summary>
    public string Channel { get; init; }

    /// <summary>The MCP client name when known.</summary>
    public string? Client { get; init; }

    /// <summary>Why the caller says it made the change. Caller-supplied, not a domain fact.</summary>
    public string? Reason { get; init; }

    /// <summary>Trace id of the execute span; null when no trace was recorded.</summary>
    public string? TraceId { get; init; }
}
