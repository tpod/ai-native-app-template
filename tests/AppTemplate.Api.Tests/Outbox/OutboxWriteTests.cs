using System.Net;
using AppTemplate.Api.Data;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;

namespace AppTemplate.Api.Tests.Outbox;

/// <summary>An execute writes one delivery row per raised event and subscriber, in the same save as the change.</summary>
public sealed class OutboxWriteTests : OutboxTestBase
{
    [Fact]
    public async Task Execute_NoteCreated_WritesOneDeliveryPerSubscriber()
    {
        await CreateNote("hello");

        var rows = await Deliveries();

        rows.Count.ShouldBe(1);
        rows[0].Subscriber.ShouldBe("handler:note-recorder");
        rows[0].Status.ShouldBe(OutboxStatus.Pending);
        rows[0].Attempts.ShouldBe(0);
        rows[0].HistoryEntry.EventType.ShouldBe("note.created");
        rows[0].AggregateType.ShouldBe(rows[0].HistoryEntry.AggregateType);
        rows[0].AggregateId.ShouldBe(rows[0].HistoryEntry.AggregateId);
    }

    [Fact]
    public async Task Preview_NoteCreated_WritesNoDeliveries()
    {
        (await Preview("fixture_create_note", new { text = "hello" })).ShouldSucceed();

        (await Deliveries()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Execute_StaleToken_WritesNoDeliveries()
    {
        var id = await CreateNote("hello");
        var p = await Preview("fixture_edit_note", new { noteId = id, text = "world" });
        (await Execute("fixture_edit_note", new { noteId = id, text = "world" }, TokenOf(p))).ShouldSucceed();

        var second = await Execute("fixture_edit_note", new { noteId = id, text = "world" }, TokenOf(p));

        second.ShouldFailWith("preview.stale", HttpStatusCode.Conflict);
        (await Deliveries()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Execute_EventWithoutSubscribers_WritesNoDeliveries()
    {
        var id = await CreateNote("hello");

        var (_, e) = await PreviewAndExecute("fixture_lock_note", new { noteId = id });

        e.ShouldSucceed();
        var rows = await Deliveries();
        rows.Count.ShouldBe(1);
        rows[0].HistoryEntry.EventType.ShouldBe("note.created");
    }
}
