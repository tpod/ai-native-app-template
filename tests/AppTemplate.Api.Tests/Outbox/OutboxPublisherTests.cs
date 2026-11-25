using AppTemplate.Api.Actions.Outbox;
using AppTemplate.Api.Data;
using AppTemplate.Api.Tests.Fixtures.Notes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.Tests.Outbox;

/// <summary>Publishers receive the stored event as an envelope, filtered by event name when they ask.</summary>
public sealed class OutboxPublisherTests : OutboxTestBase
{
    private RecordingPublisher Bus => Factory.Services.GetRequiredService<RecordingPublisher>();

    private EditsOnlyPublisher Edits => Factory.Services.GetRequiredService<EditsOnlyPublisher>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddEventPublisher<RecordingPublisher>(ServiceLifetime.Singleton);
        services.AddEventPublisher<EditsOnlyPublisher>(ServiceLifetime.Singleton);
    }

    [Fact]
    public async Task Drain_Publisher_ReceivesEnvelopeOfEveryEvent()
    {
        var id = await CreateNote("hello");
        await EditNote(id, "world");

        await DrainOutboxAsync();

        var received = Bus.Received;
        received.Count.ShouldBe(2);
        var created = received[0];
        var row = await Delivery("publisher:note-bus", 0);
        created.Id.ShouldBe(row.Id);
        created.Event.ShouldBe("note.created");
        created.EventVersion.ShouldBe(1);
        created.AggregateId.ShouldBe(id);
        created.AggregateType.ShouldBe(row.HistoryEntry.AggregateType);
        created.AggregateVersion.ShouldBe(row.HistoryEntry.AggregateVersion);
        created.OccurredAt.ShouldBe(row.HistoryEntry.OccurredAt);
        created.TraceId.ShouldBe(row.HistoryEntry.TraceId);
        created.Data.GetProperty("text").GetString().ShouldBe("hello");
        received[1].Event.ShouldBe("note.edited");
    }

    [Fact]
    public async Task Drain_PublisherWithEventsFilter_ReceivesOnlyListed()
    {
        var id = await CreateNote("hello");
        await EditNote(id, "world");

        await DrainOutboxAsync();

        Edits.Received.Count.ShouldBe(1);
        Edits.Received[0].Event.ShouldBe("note.edited");
    }

    [Fact]
    public async Task Drain_PublisherEventVersionBumped_StillDelivers()
    {
        await CreateNote("hello");
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().History
                .ExecuteUpdateAsync(s => s.SetProperty(h => h.EventVersion, 99), Ct);
        }

        await DrainOutboxAsync();

        (await Delivery("publisher:note-bus")).Status.ShouldBe(OutboxStatus.Done);
        Bus.Received.Single().EventVersion.ShouldBe(99);
    }
}
