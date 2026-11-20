using System.Text.Json;
using AppTemplate.Api.Actions;
using AppTemplate.Domain.Common;
using Microsoft.EntityFrameworkCore;
using OneOf;

namespace AppTemplate.Api.Features.History;

/// <param name="AggregateType">The aggregate's type name, e.g. <c>Project</c>. Case-insensitive.</param>
/// <param name="AggregateId">The aggregate's id. A GUID in any standard format matches.</param>
/// <param name="AfterId">Only entries with a larger id: pass the last id of the previous oldest-first page.</param>
/// <param name="Limit">Entries per page, 1 to 200; 50 when omitted.</param>
/// <param name="NewestFirst">True for newest first, to read the latest events without paging through all of them.</param>
/// <param name="BeforeId">Only entries with a smaller id: pass the last id of the previous newest-first page.</param>
public sealed record GetHistoryInput(
    string AggregateType, string AggregateId, long? AfterId = null, int? Limit = null, bool? NewestFirst = null, long? BeforeId = null);

/// <param name="Reason">Why the caller says it made the change. Caller-supplied, not a domain fact.</param>
/// <param name="TraceId">Leads to the execute's trace and logs while telemetry is retained.</param>
/// <param name="Payload">The event as recorded, e.g. <c>{ "oldName": "A", "newName": "B" }</c>.</param>
public sealed record HistoryEntryDto(
    long Id,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAt,
    int AggregateVersion,
    string Action,
    string Channel,
    string? Client,
    string? Reason,
    string? TraceId,
    Guid ExecutionId,
    JsonElement Payload);

[Query(
    "get_history",
    "Business history of one aggregate, oldest first: each committed event with its action, channel, time, traceId and the caller's optional reason. reason is caller-supplied, not a domain fact. traceId leads to telemetry only while traces are retained. Page with afterId; or pass newestFirst = true for the latest events and page back with beforeId.")]
public sealed class GetHistory : IQuery<GetHistoryInput, IReadOnlyList<HistoryEntryDto>>
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public async Task<OneOf<IReadOnlyList<HistoryEntryDto>, UnknownAggregateType>> Run(GetHistoryInput input, ActionContext ctx)
    {
        List<string> known = [.. ctx.Db.Model.GetEntityTypes()
            .Where(t => typeof(IAggregateRoot).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType.Name)
            .Order(StringComparer.Ordinal)];
        var type = known.Find(k => string.Equals(k, input.AggregateType, StringComparison.OrdinalIgnoreCase));
        if (type is null)
        {
            return new UnknownAggregateType(input.AggregateType, known);
        }

        // The change set writes Guid keys as lowercase "D"; accept any format an agent may have copied.
        var id = Guid.TryParse(input.AggregateId, out var guid) ? guid.ToString("D") : input.AggregateId;
        var afterId = input.AfterId ?? 0;
        var beforeId = input.BeforeId ?? long.MaxValue;

        var matching = ctx.Db.History.AsNoTracking()
            .Where(h => h.AggregateType == type && h.AggregateId == id && h.Id > afterId && h.Id < beforeId);
        var rows = await (input.NewestFirst == true ? matching.OrderByDescending(h => h.Id) : matching.OrderBy(h => h.Id))
            .Take(input.Limit ?? DefaultLimit)
            .ToListAsync(ctx.CancellationToken);

        return rows.ConvertAll(h => new HistoryEntryDto(
            h.Id, h.EventType, h.EventVersion, h.OccurredAt, h.AggregateVersion, h.Action, h.Channel, h.Client, h.Reason,
            h.TraceId, h.ExecutionId, JsonDocument.Parse(h.Payload).RootElement.Clone()));
    }
}
