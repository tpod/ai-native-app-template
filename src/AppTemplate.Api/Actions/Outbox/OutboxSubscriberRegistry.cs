using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using AppTemplate.Api.Actions.Events;

namespace AppTemplate.Api.Actions.Outbox;

/// <summary>
/// Every outbox subscriber, keyed by its stable name. The pipeline asks it which subscribers an event fans out to;
/// the processor asks it which class to resolve for a row and which CLR type an <c>[Event]</c> name and version
/// deserialize into. Built once; an unnamed, malformed or duplicate subscriber name fails at startup.
/// </summary>
public sealed partial class OutboxSubscriberRegistry
{
    private readonly Dictionary<string, HandlerRegistration> _handlers = new(StringComparer.Ordinal);
    private readonly List<(string Key, HashSet<string>? Events)> _publishers = [];
    private readonly Dictionary<(string Name, int Version), Type> _eventTypes = [];
    private readonly ConcurrentDictionary<Type, string[]> _handlerKeysByEvent = new();

    internal OutboxSubscriberRegistry(IEnumerable<SubscriberDescriptor> descriptors)
    {
        var owners = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors.DistinctBy(d => (d.Kind, d.ImplementationType)))
        {
            var attribute = descriptor.ImplementationType.GetCustomAttribute<SubscriberAttribute>(inherit: false)
                ?? throw new InvalidOperationException(
                    $"{descriptor.ImplementationType.FullName} has no [Subscriber] attribute. Add [Subscriber(\"<stable-name>\")] so its delivery rows keep a stable key.");
            if (!NamePattern().IsMatch(attribute.Name))
            {
                throw new InvalidOperationException(
                    $"Subscriber name '{attribute.Name}' on {descriptor.ImplementationType.FullName} must match ^[a-z0-9-]+$.");
            }

            if (owners.TryGetValue(attribute.Name, out var other))
            {
                throw new InvalidOperationException(
                    $"Subscriber name '{attribute.Name}' is used by both {other.FullName} and {descriptor.ImplementationType.FullName}. Names must be unique.");
            }

            owners[attribute.Name] = descriptor.ImplementationType;
            if (descriptor.Kind == SubscriberKind.Handler)
            {
                var key = $"handler:{attribute.Name}";
                _handlers[key] = new HandlerRegistration(key, descriptor.ImplementationType, descriptor.EventTypes);
                foreach (var eventType in descriptor.EventTypes)
                {
                    var name = EventNames.Of(eventType);
                    _eventTypes[(name.Name, name.Version)] = eventType;
                }
            }
            else
            {
                _publishers.Add(($"publisher:{attribute.Name}", attribute.Events is null ? null : new HashSet<string>(attribute.Events, StringComparer.Ordinal)));
            }
        }
    }

    /// <summary>The subscriber keys an event fans out to: matching handlers, then matching publishers.</summary>
    public IReadOnlyList<string> KeysFor(Type eventType, string eventName)
    {
        var handlerKeys = _handlerKeysByEvent.GetOrAdd(eventType, t =>
            [.. _handlers.Values.Where(h => h.EventTypes.Contains(t)).Select(h => h.Key).Order(StringComparer.Ordinal)]);
        if (_publishers.Count == 0)
        {
            return handlerKeys;
        }

        return [.. handlerKeys, .. _publishers.Where(p => p.Events is null || p.Events.Contains(eventName)).Select(p => p.Key)];
    }

    public bool TryGetHandler(string key, out HandlerRegistration registration)
        => _handlers.TryGetValue(key, out registration!);

    /// <summary>The CLR type registered handlers know for this event name and version; null when none does.</summary>
    public Type? EventTypeFor(string eventName, int eventVersion)
        => _eventTypes.GetValueOrDefault((eventName, eventVersion));

    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex NamePattern();
}
