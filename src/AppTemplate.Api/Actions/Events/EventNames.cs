using System.Collections.Concurrent;
using System.Reflection;
using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Actions.Events;

/// <summary>Resolves the stable <see cref="EventAttribute"/> of a domain event type, cached per type.</summary>
internal static class EventNames
{
    private static readonly ConcurrentDictionary<Type, EventAttribute?> Cache = new();

    /// <exception cref="InvalidOperationException">The event type has no <see cref="EventAttribute"/>: a programming error.</exception>
    public static EventAttribute Of(Type eventType)
        => Cache.GetOrAdd(eventType, static t => t.GetCustomAttribute<EventAttribute>(inherit: false))
            ?? throw new InvalidOperationException(
                $"Domain event {eventType.FullName} has no [Event] attribute. Add [Event(\"<aggregate>.<verb>\", 1)] so it can be stored in history.");
}
