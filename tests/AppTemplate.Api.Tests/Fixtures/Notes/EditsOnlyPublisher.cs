using AppTemplate.Api.Actions.Outbox;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

/// <summary>Singleton publisher that subscribes to <c>note.edited</c> only.</summary>
[Subscriber("note-edits", Events = ["note.edited"])]
public sealed class EditsOnlyPublisher : IEventPublisher
{
    public List<OutboxEnvelope> Received { get; } = [];

    public Task Publish(OutboxEnvelope envelope, CancellationToken ct)
    {
        lock (Received)
        {
            Received.Add(envelope);
        }

        return Task.CompletedTask;
    }
}
