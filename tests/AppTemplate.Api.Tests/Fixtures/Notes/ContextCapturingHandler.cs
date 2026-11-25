using AppTemplate.Api.Actions.Events;
using AppTemplate.Api.Actions.Outbox;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

/// <summary>Scoped handler that records the <see cref="OutboxDeliveryContext"/> it was delivered with.</summary>
[Subscriber("note-context")]
public sealed class ContextCapturingHandler(OutboxDeliveryContext context, ContextCapturingHandler.Sink sink) : IDomainEventHandler<NoteCreated>
{
    public Task Handle(NoteCreated domainEvent, CancellationToken ct)
    {
        lock (sink.Seen)
        {
            sink.Seen.Add((context.DeliveryId, context.Attempt));
        }

        return Task.CompletedTask;
    }

    /// <summary>Singleton the scoped handler writes to, so the test can read what it saw.</summary>
    public sealed class Sink
    {
        public List<(long DeliveryId, int Attempt)> Seen { get; } = [];
    }
}
