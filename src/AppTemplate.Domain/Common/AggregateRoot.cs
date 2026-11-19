namespace AppTemplate.Domain.Common;

public abstract class AggregateRoot<TId> : Entity<TId>, IAggregateRoot where TId : struct
{
    private readonly List<IDomainEvent> _events = [];

    public int Version { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _events;

    public void ClearDomainEvents() => _events.Clear();

    protected void Raise(IDomainEvent e) => _events.Add(e);

    void IAggregateRoot.IncrementVersion() => Version++;
}
