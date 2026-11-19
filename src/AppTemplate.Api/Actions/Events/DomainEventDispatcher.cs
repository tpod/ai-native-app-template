using System.Collections.Concurrent;
using System.Reflection;
using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Actions.Events;

/// <summary>
/// Calls every <see cref="IDomainEventHandler{TEvent}"/> registered for each event's runtime type, in event order.
/// </summary>
/// <remarks>
/// Scoped: the pipeline resolves it from the execute scope, so handlers share that scope (and its DbContext).
/// Dispatch runs after <c>SaveChanges</c> has committed. A handler exception propagates to the caller (a 500 on HTTP)
/// but does not undo the commit. There is no outbox: delivery is in-process and best-effort by design.
/// </remarks>
public sealed class DomainEventDispatcher(IServiceProvider sp)
{
    private static readonly MethodInfo DispatchOneMethod =
        typeof(DomainEventDispatcher).GetMethod(nameof(DispatchOne), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, IDomainEvent, CancellationToken, Task>> Dispatchers = new();

    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(events);
        foreach (var domainEvent in events)
        {
            var dispatch = Dispatchers.GetOrAdd(domainEvent.GetType(), static type =>
                DispatchOneMethod.MakeGenericMethod(type)
                    .CreateDelegate<Func<IServiceProvider, IDomainEvent, CancellationToken, Task>>());
            await dispatch(sp, domainEvent, ct);
        }
    }

    private static async Task DispatchOne<TEvent>(IServiceProvider services, IDomainEvent domainEvent, CancellationToken ct)
        where TEvent : IDomainEvent
    {
        foreach (var handler in services.GetServices<IDomainEventHandler<TEvent>>())
        {
            await handler.Handle((TEvent)domainEvent, ct);
        }
    }
}
