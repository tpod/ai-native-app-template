using AppTemplate.Api.Actions.Outbox;
using AppTemplate.Api.Tests.Fixtures.Notes;
using AppTemplate.Api.Tests.Infrastructure;
using AppTemplate.Api.Tests.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.Tests.Features.Outbox;

/// <summary><c>list_outbox_deliveries</c> lets an agent find dead or stuck deliveries without reading logs.</summary>
public sealed class ListOutboxDeliveriesTests : OutboxTestBase
{
    private FlakyNoteHandler Flaky => Factory.Services.GetRequiredService<FlakyNoteHandler>();

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddEventHandler<FlakyNoteHandler>(ServiceLifetime.Singleton);

    [Fact]
    [Covers("list_outbox_deliveries", Case.HappyPath)]
    public async Task Query_StatusDead_ReturnsOnlyDeadNewestFirst()
    {
        Flaky.FailCreated = true;
        await CreateNote("a");
        await CreateNote("b");
        for (var round = 0; round < 12; round++)
        {
            await DrainOutboxAsync();
            Factory.Time.Advance(TimeSpan.FromHours(1));
        }

        var q = await Query("list_outbox_deliveries", new { status = "dead" });

        q.ShouldSucceed();
        var items = q.Body.EnumerateArray().ToList();
        items.Count.ShouldBe(2);
        items.ShouldAllBe(i => i.GetProperty("status").GetString() == "dead" && i.GetProperty("subscriber").GetString() == "handler:note-flaky");
        items[0].GetProperty("id").GetInt64().ShouldBeGreaterThan(items[1].GetProperty("id").GetInt64());
        items[0].GetProperty("event").GetString().ShouldBe("note.created");
        items[0].GetProperty("attempts").GetInt32().ShouldBe(10);
        items[0].GetProperty("lastError").GetString().ShouldNotBeNullOrEmpty();
        items[0].TryGetProperty("payload", out _).ShouldBeFalse();
    }

    [Fact]
    [Covers("list_outbox_deliveries", "validation.failed")]
    public async Task Query_LimitZero_ReturnsFieldError()
    {
        var q = await Query("list_outbox_deliveries", new { limit = 0 });

        q.ShouldHaveFieldError("limit", "outbox.limit_out_of_range");
    }

    [Fact]
    public async Task Query_BeforeId_PagesBack()
    {
        await CreateNote("a");
        await CreateNote("b");
        await CreateNote("c");
        var first = (await Query("list_outbox_deliveries", new { subscriber = "handler:note-recorder", limit = 2 })).Body.EnumerateArray().ToList();
        first.Count.ShouldBe(2);

        var next = await Query("list_outbox_deliveries", new { subscriber = "handler:note-recorder", limit = 2, beforeId = first[1].GetProperty("id").GetInt64() });

        var page = next.Body.EnumerateArray().ToList();
        page.Count.ShouldBe(1);
        page[0].GetProperty("id").GetInt64().ShouldBeLessThan(first[1].GetProperty("id").GetInt64());
    }
}
