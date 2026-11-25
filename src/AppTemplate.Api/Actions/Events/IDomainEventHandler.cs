using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Actions.Events;

/// <summary>
/// Reacts to a domain event after the execute that raised it has committed, delivered by the outbox worker in its own
/// DI scope. Register the class with <c>services.AddEventHandler&lt;T&gt;()</c> and give it a <c>[Subscriber]</c> name.
/// Delivery is at least once and per-aggregate ordered: make the handler idempotent (<c>OutboxDeliveryContext</c>
/// carries the delivery id). A handler that changes state calls an action through <c>ActionRunner</c>.
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken ct);
}
