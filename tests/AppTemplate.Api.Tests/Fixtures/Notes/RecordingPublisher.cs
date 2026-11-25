using AppTemplate.Api.Actions.Outbox;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

/// <summary>Singleton publisher that records every envelope it is given, like a bus adapter would send them.</summary>
[Subscriber("note-bus")]
public sealed class RecordingPublisher : IEventPublisher
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
