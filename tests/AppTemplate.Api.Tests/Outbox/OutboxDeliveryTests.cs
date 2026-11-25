using AppTemplate.Api.Data;
using AppTemplate.Api.Tests.Fixtures.Notes;
using Shouldly;

namespace AppTemplate.Api.Tests.Outbox;

/// <summary>The outbox delivers committed events to handlers, once each, in per-aggregate order.</summary>
public sealed class OutboxDeliveryTests : OutboxTestBase
{
    [Fact]
    public async Task Drain_NoteCreated_DeliversToHandlerAndMarksDone()
    {
        await CreateNote("hello");

        var claimed = await DrainOutboxAsync();

        claimed.ShouldBe(1);
        Recorder.Received.OfType<NoteCreated>().Single().Text.ShouldBe("hello");
        var row = (await Deliveries()).Single();
        row.Status.ShouldBe(OutboxStatus.Done);
        row.Attempts.ShouldBe(1);
        row.CompletedAt.ShouldBe(Factory.Time.GetUtcNow());
    }

    [Fact]
    public async Task Execute_BeforeDrain_HandlerNotCalled()
    {
        await CreateNote("hello");

        Recorder.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task Drain_ThreeEditsOneNote_DeliversInOrder()
    {
        var id = await CreateNote("v0");
        await EditNote(id, "v1");
        await EditNote(id, "v2");
        await EditNote(id, "v3");

        await DrainOutboxAsync();

        Recorder.Received.Count.ShouldBe(4);
        Recorder.Received.OfType<NoteEdited>().Select(e => e.NewText).ShouldBe(["v1", "v2", "v3"]);
        (await Deliveries()).ShouldAllBe(d => d.Status == OutboxStatus.Done);
    }

    [Fact]
    public async Task Drain_Twice_DeliversOnce()
    {
        await CreateNote("hello");
        await DrainOutboxAsync();

        var second = await DrainOutboxAsync();

        second.ShouldBe(0);
        Recorder.Received.Count.ShouldBe(1);
    }
}
