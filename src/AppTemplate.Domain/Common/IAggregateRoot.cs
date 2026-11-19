namespace AppTemplate.Domain.Common;

public interface IAggregateRoot
{
    int Version { get; }
    IReadOnlyList<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
    void IncrementVersion();
}
