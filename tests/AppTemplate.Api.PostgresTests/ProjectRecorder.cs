using AppTemplate.Api.Actions.Events;
using AppTemplate.Api.Actions.Outbox;
using AppTemplate.Domain.Common;
using AppTemplate.Domain.Projects;

namespace AppTemplate.Api.PostgresTests;

/// <summary>Singleton handler that records project events delivered by the outbox.</summary>
[Subscriber("project-recorder")]
public sealed class ProjectRecorder : IDomainEventHandler<ProjectCreated>, IDomainEventHandler<ProjectRenamed>
{
    public List<IDomainEvent> Received { get; } = [];

    public Task Handle(ProjectCreated domainEvent, CancellationToken ct) => Record(domainEvent);

    public Task Handle(ProjectRenamed domainEvent, CancellationToken ct) => Record(domainEvent);

    private Task Record(IDomainEvent domainEvent)
    {
        lock (Received)
        {
            Received.Add(domainEvent);
        }

        return Task.CompletedTask;
    }
}
