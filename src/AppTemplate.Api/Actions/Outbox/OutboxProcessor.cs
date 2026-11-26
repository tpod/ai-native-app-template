using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using AppTemplate.Api.Actions.Events;
using AppTemplate.Api.Actions.Telemetry;
using AppTemplate.Api.Data;
using AppTemplate.Domain.Common;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using OneOf;

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
    IOptions<JsonOptions> jsonOptions,
    OutboxMetrics metrics,
    ILogger<OutboxProcessor> logger)
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
            try
            {
                await DeliverAsync(row, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // One row's unexpected failure must not stop the rest of the batch.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // The row stays in flight; its lease expires, it is reclaimed and, past MaxAttempts, dies.
                LogRowFailed(ex, row.Id, row.Subscriber);
            }
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

    /// <summary>
    /// Delivers one claimed row and settles it: done, retry after backoff, or dead. A row that cannot be delivered at
    /// all (unknown subscriber or event version, unreadable payload) is dead at once. Host cancellation settles
    /// nothing: the row stays in flight and is reclaimed when its lease expires.
    /// </summary>
    private async Task DeliverAsync(OutboxDelivery row, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var context = services.GetRequiredService<OutboxDeliveryContext>();
        context.DeliveryId = row.Id;
        context.Attempt = row.Attempts;
        var store = services.GetRequiredService<OutboxStore>();

        // The batch's lease ran from claim time; a row late in a slow batch may have lost it to another worker.
        if (await store.RenewLeaseAsync(row.Id, row.Attempts, time.GetUtcNow() + Options.LeaseDuration, ct) == 0)
        {
            LogLeaseLost(row.Id, row.Subscriber);
            return;
        }

        using var activity = StartSpan(row);

        // A row that keeps crashing the worker before it can settle is reclaimed over and over; stop it here.
        if (row.Attempts > Options.MaxAttempts)
        {
            await DeadAsync(store, row, $"Exceeded {Options.MaxAttempts} attempts without settling.", activity, ct);
            return;
        }

        if (Prepare(row).TryPickT1(out var undeliverable, out var delivery))
        {
            await DeadAsync(store, row, undeliverable, activity, ct);
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Options.HandlerTimeout);
        string error;
        try
        {
            // WaitAsync stops waiting at the timeout even when the subscriber ignores its token.
            await delivery(services, timeout.Token).WaitAsync(timeout.Token);
            var now = time.GetUtcNow();
            await store.CompleteAsync(row.Id, row.Attempts, now, ct);
            Settle(activity, OutboxTelemetry.OutcomeDone, error: null, lag: now - row.HistoryEntry.OccurredAt);
            return;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            error = $"Timed out after {Options.HandlerTimeout}.";
        }
#pragma warning disable CA1031 // A subscriber's failure is recorded on its row; it must not stop the batch.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            error = $"{ex.GetType().Name}: {ex.Message}";
        }

        if (row.Attempts >= Options.MaxAttempts)
        {
            await DeadAsync(store, row, error, activity, ct);
        }
        else
        {
            await store.FailAsync(row.Id, row.Attempts, error, time.GetUtcNow() + Backoff(row.Attempts, Options), ct);
            Settle(activity, OutboxTelemetry.OutcomeRetry, error, lag: null);
        }
    }

    private async Task DeadAsync(OutboxStore store, OutboxDelivery row, string reason, Activity? activity, CancellationToken ct)
    {
        LogDead(row.Id, row.Subscriber, row.HistoryEntry.EventType, reason);
        await store.MarkDeadAsync(row.Id, row.Attempts, reason, ct);
        Settle(activity, OutboxTelemetry.OutcomeDead, reason, lag: null);
    }

    /// <summary>A root span per attempt, linked to the execute's trace when history recorded one.</summary>
    private static Activity? StartSpan(OutboxDelivery row)
    {
        var entry = row.HistoryEntry;
        ActivityLink[]? links = null;
        if (entry.TraceId is { Length: 32 } traceId && traceId.All(char.IsAsciiHexDigitLower))
        {
            // History keeps only the trace id, so the link points at the trace rather than a specific span.
            links = [new ActivityLink(new ActivityContext(ActivityTraceId.CreateFromString(traceId), default, ActivityTraceFlags.None))];
        }

        var activity = ActionTelemetry.Source.StartActivity(
            OutboxTelemetry.SpanPrefix + row.Subscriber, ActivityKind.Consumer, parentContext: default, links: links);
        activity?.SetTag(OutboxTelemetry.TagSubscriber, row.Subscriber)
            .SetTag(OutboxTelemetry.TagAttempt, row.Attempts)
            .SetTag(OutboxTelemetry.TagEventName, entry.EventType)
            .SetTag(ActionTelemetry.TagAggregateType, row.AggregateType);
        return activity;
    }

    private void Settle(Activity? activity, string outcome, string? error, TimeSpan? lag)
    {
        activity?.SetTag(OutboxTelemetry.TagOutcome, outcome);
        if (error is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, error);
        }

        metrics.Record(outcome, lag);
    }

    /// <summary>Resolves the subscriber and decodes the event, or says why this row can never be delivered.</summary>
    private OneOf<Func<IServiceProvider, CancellationToken, Task>, string> Prepare(OutboxDelivery row)
    {
        if (registry.TryGetPublisher(row.Subscriber, out var publisherType))
        {
            return PreparePublish(row, publisherType);
        }

        if (!registry.TryGetHandler(row.Subscriber, out var registration))
        {
            return $"No subscriber '{row.Subscriber}' is registered.";
        }

        var entry = row.HistoryEntry;
        if (registry.EventTypeFor(entry.EventType, entry.EventVersion) is not { } eventType)
        {
            return $"No handler knows event {entry.EventType} v{entry.EventVersion}; drain the outbox before bumping an event version.";
        }

        IDomainEvent? domainEvent;
        try
        {
            domainEvent = (IDomainEvent?)JsonSerializer.Deserialize(entry.Payload, eventType, jsonOptions.Value.SerializerOptions);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // JsonException: malformed or mismatched payload. The others: an event type the serializer cannot
            // construct (for example a value-object property without a usable constructor).
            return $"Payload of {entry.EventType} v{entry.EventVersion} cannot be read as {eventType.Name}: {ex.GetType().Name}: {ex.Message}";
        }

        if (domainEvent is null)
        {
            return $"Payload of {entry.EventType} v{entry.EventVersion} is empty.";
        }

        var handle = Handlers.GetOrAdd(eventType, static type =>
            HandleOneMethod.MakeGenericMethod(type).CreateDelegate<Func<object, IDomainEvent, CancellationToken, Task>>());
        return (Func<IServiceProvider, CancellationToken, Task>)((services, token) =>
            handle(services.GetRequiredService(registration.HandlerType), domainEvent, token));
    }

    /// <summary>Publishers get the stored payload as is, so they need no CLR event type and survive version bumps.</summary>
    private static OneOf<Func<IServiceProvider, CancellationToken, Task>, string> PreparePublish(OutboxDelivery row, Type publisherType)
    {
        var entry = row.HistoryEntry;
        JsonElement data;
        try
        {
            using var document = JsonDocument.Parse(entry.Payload);
            data = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return $"Payload of {entry.EventType} v{entry.EventVersion} is not valid JSON: {ex.Message}";
        }

        var envelope = new OutboxEnvelope(
            row.Id, entry.EventType, entry.EventVersion, entry.AggregateType, entry.AggregateId, entry.AggregateVersion,
            entry.OccurredAt, entry.TraceId, data);
        return (Func<IServiceProvider, CancellationToken, Task>)((services, token) =>
            ((IEventPublisher)services.GetRequiredService(publisherType)).Publish(envelope, token));
    }

    /// <summary>The wait before retry number <paramref name="attempts"/> + 1: the base, doubling, capped.</summary>
    internal static TimeSpan Backoff(int attempts, OutboxOptions o)
    {
        ArgumentNullException.ThrowIfNull(o);
        var exponent = Math.Clamp(attempts - 1, 0, 30);
        var ticks = o.BackoffBase.Ticks * Math.Pow(2, exponent);
        return ticks >= o.BackoffCap.Ticks ? o.BackoffCap : TimeSpan.FromTicks((long)ticks);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox delivery {DeliveryId} to {Subscriber} failed before it could settle; it stays in flight until its lease expires")]
    private partial void LogRowFailed(Exception exception, long deliveryId, string subscriber);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox delivery {DeliveryId} to {Subscriber} lost its lease while waiting in the batch; another worker has it")]
    private partial void LogLeaseLost(long deliveryId, string subscriber);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox delivery {DeliveryId} to {Subscriber} of {EventName} is dead: {Reason}")]
    private partial void LogDead(long deliveryId, string subscriber, string eventName, string reason);

    private static Task HandleOne<TEvent>(object handler, IDomainEvent domainEvent, CancellationToken ct)
        where TEvent : IDomainEvent
        => ((IDomainEventHandler<TEvent>)handler).Handle((TEvent)domainEvent, ct);
}
