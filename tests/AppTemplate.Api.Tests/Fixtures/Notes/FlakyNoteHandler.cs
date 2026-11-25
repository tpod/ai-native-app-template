using AppTemplate.Api.Actions.Events;
using AppTemplate.Api.Actions.Outbox;
using AppTemplate.Domain.Common;

namespace AppTemplate.Api.Tests.Fixtures.Notes;

/// <summary>Singleton handler whose failures a test sets: fail N times, fail every NoteCreated, or hang.</summary>
[Subscriber("note-flaky")]
public sealed class FlakyNoteHandler : IDomainEventHandler<NoteCreated>, IDomainEventHandler<NoteEdited>
{
    private int _failTimes;

    /// <summary>Throw on the next N deliveries, whatever the event.</summary>
    public int FailTimes { get => _failTimes; set => _failTimes = value; }

    /// <summary>Throw on every NoteCreated delivery.</summary>
    public bool FailCreated { get; set; }

    /// <summary>Wait until cancelled instead of returning.</summary>
    public bool Hang { get; set; }

    /// <summary>Completes when a delivery starts, so a test can act while the handler runs.</summary>
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<IDomainEvent> Received { get; } = [];

    public Task Handle(NoteCreated domainEvent, CancellationToken ct) => Run(domainEvent, FailCreated, ct);

    public Task Handle(NoteEdited domainEvent, CancellationToken ct) => Run(domainEvent, alwaysFail: false, ct);

    private async Task Run(IDomainEvent domainEvent, bool alwaysFail, CancellationToken ct)
    {
        Started.TrySetResult();
        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, ct);
        }

        if (alwaysFail || Interlocked.Decrement(ref _failTimes) >= 0)
        {
            throw new InvalidOperationException("flaky handler failed on purpose");
        }

        lock (Received)
        {
            Received.Add(domainEvent);
        }
    }
}
