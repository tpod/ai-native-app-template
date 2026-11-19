using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Actions.Events;

/// <summary>
/// Reacts to a domain event after the execute that raised it has committed. Register one per concrete event type
/// (<c>IDomainEventHandler&lt;ProjectRenamed&gt;</c>); the container does not match handlers by variance.
/// </summary>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task Handle(TEvent domainEvent, CancellationToken ct);
}
