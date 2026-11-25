namespace AppTemplate.Api.Actions.Outbox;

/// <summary>
/// Sends committed events to an external system (a broker or bus). Register with
/// <c>services.AddEventPublisher&lt;T&gt;()</c> and a <see cref="SubscriberAttribute"/> name; its <c>Events</c> filter
/// limits which events it receives. Delivery is at least once, retried and ordered per aggregate like handlers;
/// receivers dedupe on <see cref="OutboxEnvelope.Id"/>.
/// </summary>
public interface IEventPublisher
{
    Task Publish(OutboxEnvelope envelope, CancellationToken ct);
}
