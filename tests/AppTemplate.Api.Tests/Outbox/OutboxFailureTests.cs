using AppTemplate.Api.Actions.Outbox;
using AppTemplate.Api.Data;
using AppTemplate.Api.Tests.Fixtures.Notes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.Tests.Outbox;

/// <summary>Failed deliveries retry with backoff, die after the last attempt, and never block other subscribers.</summary>
public sealed class OutboxFailureTests : OutboxTestBase
{
    private const string FlakyKey = "handler:note-flaky";

    private FlakyNoteHandler Flaky => Factory.Services.GetRequiredService<FlakyNoteHandler>();

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddEventHandler<FlakyNoteHandler>(ServiceLifetime.Singleton);

    [Fact]
    public async Task Drain_HandlerThrows_RetriesAfterBackoff()
    {
        Flaky.FailTimes = 1;
        await CreateNote("a");

        await DrainOutboxAsync();

        var row = await Delivery(FlakyKey);
        row.Status.ShouldBe(OutboxStatus.Pending);
        row.Attempts.ShouldBe(1);
        row.NextAttemptAt.ShouldBe(Factory.Time.GetUtcNow().AddSeconds(10));
        row.LastError.ShouldNotBeNull().ShouldContain("failed on purpose");

        Factory.Time.Advance(TimeSpan.FromSeconds(10));
        await DrainOutboxAsync();

        (await Delivery(FlakyKey)).Status.ShouldBe(OutboxStatus.Done);
        Flaky.Received.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Drain_HandlerAlwaysFails_DeadAfterMaxAttempts()
    {
        Flaky.FailCreated = true;
        await CreateNote("a");

        await DrainUntilSettled();

        var row = await Delivery(FlakyKey);
        row.Status.ShouldBe(OutboxStatus.Dead);
        row.Attempts.ShouldBe(10);
    }

    [Fact]
    public async Task Drain_DeadRow_UnblocksLaterRowsOfAggregate()
    {
        Flaky.FailCreated = true;
        var id = await CreateNote("a");
        await EditNote(id, "b");

        await DrainUntilSettled();

        (await Delivery(FlakyKey, 0)).Status.ShouldBe(OutboxStatus.Dead);
        (await Delivery(FlakyKey, 1)).Status.ShouldBe(OutboxStatus.Done);
        Flaky.Received.OfType<NoteEdited>().Single().NewText.ShouldBe("b");
    }

    [Fact]
    public async Task Drain_EarlierRowRetrying_BlocksLaterRowOfSameAggregate()
    {
        Flaky.FailTimes = 1;
        var id = await CreateNote("a");
        await EditNote(id, "b");

        await DrainOutboxAsync();

        (await Delivery(FlakyKey, 0)).Attempts.ShouldBe(1);
        var later = await Delivery(FlakyKey, 1);
        later.Status.ShouldBe(OutboxStatus.Pending);
        later.Attempts.ShouldBe(0);
        Flaky.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task Drain_FailingSubscriber_DoesNotBlockOtherSubscriber()
    {
        Flaky.FailCreated = true;
        await CreateNote("a");

        await DrainOutboxAsync();

        (await Delivery("handler:note-recorder")).Status.ShouldBe(OutboxStatus.Done);
        (await Delivery(FlakyKey)).Status.ShouldBe(OutboxStatus.Pending);
    }

    [Fact]
    public async Task Drain_LeaseExpired_ReclaimsInFlightRow()
    {
        await CreateNote("a");
        var now = Factory.Time.GetUtcNow();
        await WithStore(store => store.ClaimAsync(10, now, now.AddSeconds(-1), Ct));

        await DrainOutboxAsync();

        var row = await Delivery("handler:note-recorder");
        row.Status.ShouldBe(OutboxStatus.Done);
        row.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task Drain_HandlerNoLongerRegistered_MarksDead()
    {
        await CreateNote("a");
        await AddDeliveryFor("handler:gone");

        await DrainOutboxAsync();

        var row = await Delivery("handler:gone");
        row.Status.ShouldBe(OutboxStatus.Dead);
        row.LastError.ShouldNotBeNull().ShouldContain("handler:gone");
    }

    [Fact]
    public async Task Drain_EventVersionUnknown_MarksDead()
    {
        await CreateNote("a");
        await WithDb(db => db.History.ExecuteUpdateAsync(s => s.SetProperty(h => h.EventVersion, 99), Ct));

        await DrainOutboxAsync();

        var row = await Delivery("handler:note-recorder");
        row.Status.ShouldBe(OutboxStatus.Dead);
        row.LastError.ShouldNotBeNull().ShouldContain("note.created v99");
    }

    [Fact]
    public async Task Drain_PayloadUndeserializable_MarksDeadAndDeliversOthers()
    {
        await CreateNote("first");
        await CreateNote("second");
        var firstHistoryId = (await Deliveries())[0].HistoryEntryId;
        await WithDb(db => db.History.Where(h => h.Id == firstHistoryId).ExecuteUpdateAsync(s => s.SetProperty(h => h.Payload, "not json"), Ct));

        await DrainOutboxAsync();

        (await Delivery("handler:note-recorder", 0)).Status.ShouldBe(OutboxStatus.Dead);
        (await Delivery("handler:note-recorder", 1)).Status.ShouldBe(OutboxStatus.Done);
        Recorder.Received.OfType<NoteCreated>().Single().Text.ShouldBe("second");
    }

    [Fact]
    public async Task Retention_DoneRowsOlderThanRetention_AreDeleted()
    {
        await CreateNote("a");
        await AddDeliveryFor("handler:gone");
        await DrainOutboxAsync();
        Factory.Time.Advance(TimeSpan.FromDays(8));

        var deleted = await WithStore(store => store.DeleteDoneBeforeAsync(Factory.Time.GetUtcNow().AddDays(-7), Ct));

        deleted.ShouldBe(2);
        (await Deliveries()).Single().Subscriber.ShouldBe("handler:gone");
    }

    [Fact]
    public void Backoff_Attempts_DoublesFromBaseUpToCap()
    {
        var o = new OutboxOptions();

        OutboxProcessor.Backoff(1, o).ShouldBe(TimeSpan.FromSeconds(10));
        OutboxProcessor.Backoff(2, o).ShouldBe(TimeSpan.FromSeconds(20));
        OutboxProcessor.Backoff(9, o).ShouldBe(TimeSpan.FromSeconds(2560));
        OutboxProcessor.Backoff(10, o).ShouldBe(TimeSpan.FromHours(1));
        OutboxProcessor.Backoff(40, o).ShouldBe(TimeSpan.FromHours(1));
    }

    /// <summary>Drains, then jumps past the longest backoff, until no row is left pending.</summary>
    private async Task DrainUntilSettled()
    {
        for (var round = 0; round < 20; round++)
        {
            await DrainOutboxAsync();
            if ((await Deliveries()).All(d => d.Status is OutboxStatus.Done or OutboxStatus.Dead))
            {
                return;
            }

            Factory.Time.Advance(TimeSpan.FromHours(1));
        }

        throw new InvalidOperationException("Outbox rows did not settle.");
    }

    private async Task AddDeliveryFor(string subscriber)
    {
        await WithDb(async db =>
        {
            var entry = await db.History.OrderBy(h => h.Id).FirstAsync(Ct);
            db.OutboxDeliveries.Add(new OutboxDelivery(entry, subscriber, Factory.Time.GetUtcNow()));
            return await db.SaveChangesAsync(Ct);
        });
    }

    private async Task<T> WithDb<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<T> WithStore<T>(Func<OutboxStore, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<OutboxStore>());
    }
}
