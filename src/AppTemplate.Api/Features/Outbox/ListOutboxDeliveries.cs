using AppTemplate.Api.Actions;
using AppTemplate.Api.Data;
using Microsoft.EntityFrameworkCore;
using OneOf;

namespace AppTemplate.Api.Features.Outbox;

/// <param name="Status">Only deliveries in this status; omitted lists every status.</param>
/// <param name="Subscriber">Only this subscriber key, e.g. <c>handler:project-indexer</c>.</param>
/// <param name="AggregateType">Only deliveries of this aggregate type, e.g. <c>Project</c>.</param>
/// <param name="AggregateId">Only deliveries of this aggregate id.</param>
/// <param name="Limit">Rows per page, 1 to 200; 50 when omitted.</param>
/// <param name="BeforeId">Page back: only rows with a lower id than this.</param>
public sealed record ListOutboxDeliveriesInput(
    OutboxStatus? Status = null,
    string? Subscriber = null,
    string? AggregateType = null,
    string? AggregateId = null,
    int? Limit = null,
    long? BeforeId = null);

/// <param name="Event">The <c>[Event]</c> name of the delivered event; <c>get_history</c> shows its payload.</param>
public sealed record OutboxDeliveryDto(
    long Id,
    string Subscriber,
    string Event,
    string AggregateType,
    string AggregateId,
    OutboxStatus Status,
    int Attempts,
    DateTimeOffset NextAttemptAt,
    DateTimeOffset? LeaseUntil,
    DateTimeOffset? CompletedAt,
    string? LastError,
    long HistoryEntryId);

[Query(
    "list_outbox_deliveries",
    "Outbox deliveries of committed events to subscribers (handlers, publishers), newest first, with status (pending, in_flight, done, dead), attempts and the last error. Find deliveries that are dead or stuck; get_history shows the event itself. Page back with beforeId.")]
public sealed class ListOutboxDeliveries : IQuery<ListOutboxDeliveriesInput, IReadOnlyList<OutboxDeliveryDto>>
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public async Task<OneOf<IReadOnlyList<OutboxDeliveryDto>>> Run(ListOutboxDeliveriesInput input, ActionContext ctx)
    {
        var rows = ctx.Db.OutboxDeliveries.AsNoTracking();
        if (input.Status is { } status)
        {
            rows = rows.Where(d => d.Status == status);
        }

        if (input.Subscriber is { Length: > 0 } subscriber)
        {
            rows = rows.Where(d => d.Subscriber == subscriber);
        }

        if (input.AggregateType is { Length: > 0 } aggregateType)
        {
            rows = rows.Where(d => d.AggregateType == aggregateType);
        }

        if (input.AggregateId is { Length: > 0 } aggregateId)
        {
            // The change set writes Guid keys as lowercase "D"; accept any format an agent may have copied.
            var id = Guid.TryParse(aggregateId, out var guid) ? guid.ToString("D") : aggregateId;
            rows = rows.Where(d => d.AggregateId == id);
        }

        if (input.BeforeId is { } beforeId)
        {
            rows = rows.Where(d => d.Id < beforeId);
        }

        return await rows
            .OrderByDescending(d => d.Id)
            .Take(input.Limit ?? DefaultLimit)
            .Select(d => new OutboxDeliveryDto(
                d.Id, d.Subscriber, d.HistoryEntry.EventType, d.AggregateType, d.AggregateId, d.Status, d.Attempts,
                d.NextAttemptAt, d.LeaseUntil, d.CompletedAt, d.LastError, d.HistoryEntryId))
            .ToListAsync(ctx.CancellationToken);
    }
}
