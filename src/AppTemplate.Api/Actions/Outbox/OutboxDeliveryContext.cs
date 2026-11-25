namespace AppTemplate.Api.Actions.Outbox;

/// <summary>
/// The delivery a handler is running for, scoped to that delivery. Delivery is at least once, so a handler with
/// external effects can record <see cref="DeliveryId"/> and skip a redelivery.
/// </summary>
public sealed class OutboxDeliveryContext
{
    /// <summary>The outbox row id; the same on every redelivery of this event to this subscriber.</summary>
    public long DeliveryId { get; internal set; }

    /// <summary>1 on the first delivery, higher on retries.</summary>
    public int Attempt { get; internal set; }
}
