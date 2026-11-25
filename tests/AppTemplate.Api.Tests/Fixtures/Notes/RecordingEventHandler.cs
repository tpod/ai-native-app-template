using AppTemplate.Api.Actions.Events;
using AppTemplate.Api.Actions.Outbox;
using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

/// <summary>Singleton that records every Note event delivered after an execute commits.</summary>
[Subscriber("note-recorder")]
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
