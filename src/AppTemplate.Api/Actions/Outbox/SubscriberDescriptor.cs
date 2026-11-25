namespace AppTemplate.Api.Actions.Outbox;

/// <summary>One registered subscriber, recorded in DI by <c>AddEventHandler</c> or <c>AddEventPublisher</c>.</summary>
/// <param name="EventTypes">Handlers: the event types the class handles. Publishers: empty (they match by name).</param>
internal sealed record SubscriberDescriptor(SubscriberKind Kind, Type ImplementationType, IReadOnlyList<Type> EventTypes);
