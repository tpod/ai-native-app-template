using System.Diagnostics;
using AppTemplate.Api.Actions.Outbox;
using AppTemplate.Api.Tests.Fixtures.Notes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.Tests.Outbox;

/// <summary>Every delivery attempt is a span linked to the trace of the execute that raised the event.</summary>
public sealed class OutboxTelemetryTests : OutboxTestBase
{
    private FlakyNoteHandler Flaky => Factory.Services.GetRequiredService<FlakyNoteHandler>();

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddEventHandler<FlakyNoteHandler>(ServiceLifetime.Singleton);

    [Fact]
    public async Task Drain_Success_EmitsDeliverSpanWithDoneOutcome()
    {
        await CreateNote("a");
        var row = await Delivery("handler:note-recorder");

        await DrainOutboxAsync();

        var span = DeliverSpan("handler:note-recorder", row.HistoryEntry.TraceId);
        span.GetTagItem("outbox.outcome").ShouldBe("done");
        span.GetTagItem("outbox.attempt").ShouldBe(1);
        span.GetTagItem("event.name").ShouldBe("note.created");
        span.GetTagItem("aggregate.type").ShouldBe(row.AggregateType);
    }

    [Fact]
    public async Task Drain_Failure_EmitsRetryOutcome()
    {
        Flaky.FailTimes = 1;
        await CreateNote("a");
        var row = await Delivery("handler:note-flaky");

        await DrainOutboxAsync();

        var span = DeliverSpan("handler:note-flaky", row.HistoryEntry.TraceId);
        span.GetTagItem("outbox.outcome").ShouldBe("retry");
        span.Status.ShouldBe(ActivityStatusCode.Error);
    }

    /// <summary>The single deliver span for this subscriber linked to the execute's trace (unique to this test).</summary>
    private Activity DeliverSpan(string subscriber, string? executeTraceId)
    {
        executeTraceId.ShouldNotBeNull("the execute recorded no trace id to link to");
        return Factory.Activities.All
            .Where(a => a.DisplayName == $"outbox deliver {subscriber}" && a.Links.Any(l => l.Context.TraceId.ToString() == executeTraceId))
            .ToList()
            .ShouldHaveSingleItem();
    }
}
