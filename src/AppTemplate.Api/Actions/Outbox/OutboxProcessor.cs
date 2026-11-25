using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using AppTemplate.Api.Actions.Events;
using AppTemplate.Api.Data;
using AppTemplate.Domain.Common;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Actions.Outbox;

/// <summary>
/// Claims claimable outbox rows and delivers each to its subscriber in a fresh DI scope, then settles the row.
/// The background <see cref="OutboxWorker"/> calls <see cref="RunOnceAsync"/> in a loop; tests call
/// <see cref="DrainAsync"/>.
/// </summary>
public sealed partial class OutboxProcessor(
    IServiceScopeFactory scopes,
    OutboxSubscriberRegistry registry,
    TimeProvider time,
    IOptions<OutboxOptions> options,
    IOptions<JsonOptions> jsonOptions)
{
    private const int MaxDrainRounds = 1000;

    private static readonly MethodInfo HandleOneMethod =
        typeof(OutboxProcessor).GetMethod(nameof(HandleOne), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly ConcurrentDictionary<Type, Func<object, IDomainEvent, CancellationToken, Task>> Handlers = new();

    private OutboxOptions Options => options.Value;

    /// <summary>Claims one batch and delivers it. Returns the number of rows claimed.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<OutboxDelivery> rows;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<OutboxStore>();
            var now = time.GetUtcNow();
            var ids = await store.ClaimAsync(Options.BatchSize, now, now + Options.LeaseDuration, ct);
            if (ids.Count == 0)
            {
                return 0;
            }

            rows = await store.LoadAsync([.. ids], ct);
        }

        foreach (var row in rows)
        {
            await DeliverAsync(row, ct);
        }

        return rows.Count;
    }

    /// <summary>Runs <see cref="RunOnceAsync"/> until nothing is claimable. Returns the total rows claimed.</summary>
    /// <exception cref="InvalidOperationException">Still claiming after 1000 rounds: a subscriber that never settles.</exception>
    public async Task<int> DrainAsync(CancellationToken ct)
    {
        var total = 0;
        for (var round = 0; round < MaxDrainRounds; round++)
        {
            var claimed = await RunOnceAsync(ct);
            if (claimed == 0)
            {
                return total;
            }

            total += claimed;
        }

        throw new InvalidOperationException($"The outbox still had claimable rows after {MaxDrainRounds} rounds.");
    }

    private async Task DeliverAsync(OutboxDelivery row, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<OutboxDeliveryContext>();
        context.DeliveryId = row.Id;
        context.Attempt = row.Attempts;

        var store = services.GetRequiredService<OutboxStore>();
        await HandleAsync(services, row, ct);
        await store.CompleteAsync(row.Id, row.Attempts, time.GetUtcNow(), ct);
    }

    private async Task HandleAsync(IServiceProvider services, OutboxDelivery row, CancellationToken ct)
    {
        if (!registry.TryGetHandler(row.Subscriber, out var registration))
        {
            throw new InvalidOperationException($"No subscriber '{row.Subscriber}' is registered.");
        }

        var entry = row.HistoryEntry;
        var eventType = registry.EventTypeFor(entry.EventType, entry.EventVersion)
            ?? throw new InvalidOperationException($"No handler knows event {entry.EventType} v{entry.EventVersion}.");
        var domainEvent = (IDomainEvent)(JsonSerializer.Deserialize(entry.Payload, eventType, jsonOptions.Value.SerializerOptions)
            ?? throw new InvalidOperationException($"Event {entry.EventType} v{entry.EventVersion} has an empty payload."));

        var handler = services.GetRequiredService(registration.HandlerType);
        var handle = Handlers.GetOrAdd(eventType, static type =>
            HandleOneMethod.MakeGenericMethod(type).CreateDelegate<Func<object, IDomainEvent, CancellationToken, Task>>());
        await handle(handler, domainEvent, ct);
    }

    private static Task HandleOne<TEvent>(object handler, IDomainEvent domainEvent, CancellationToken ct)
        where TEvent : IDomainEvent
        => ((IDomainEventHandler<TEvent>)handler).Handle((TEvent)domainEvent, ct);
}
