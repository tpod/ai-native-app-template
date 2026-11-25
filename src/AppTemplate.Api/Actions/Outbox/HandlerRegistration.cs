namespace AppTemplate.Api.Actions.Outbox;

/// <summary>An event handler subscriber: its delivery-row key, the class to resolve and the events it handles.</summary>
public sealed record HandlerRegistration(string Key, Type HandlerType, IReadOnlyList<Type> EventTypes);
