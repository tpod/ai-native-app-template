using AppTemplate.Api.Actions.Events;
using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

/// <summary>Singleton that records every Note event dispatched after an execute commits.</summary>
public sealed class RecordingEventHandler : IDomainEventHandler<NoteCreated>, IDomainEventHandler<NoteEdited>
{
    public List<IDomainEvent> Received { get; } = [];

    public Task Handle(NoteCreated domainEvent, CancellationToken ct) => Record(domainEvent);

    public Task Handle(NoteEdited domainEvent, CancellationToken ct) => Record(domainEvent);

    private Task Record(IDomainEvent domainEvent)
    {
        lock (Received)
        {
            Received.Add(domainEvent);
        }

        return Task.CompletedTask;
    }
}
