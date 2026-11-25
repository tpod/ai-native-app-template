using AppTemplate.Api.Actions.Outbox;
using AppTemplate.Api.Tests.Fixtures.Notes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.Tests.Outbox;

/// <summary>A handler can read which delivery it is running for, so it can dedupe a redelivery.</summary>
public sealed class OutboxDeliveryContextTests : OutboxTestBase
{
    protected override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ContextCapturingHandler.Sink>();
        services.AddEventHandler<ContextCapturingHandler>();
    }

    [Fact]
    public async Task Deliver_Handler_SeesDeliveryContext()
    {
        await CreateNote("hello");

        await DrainOutboxAsync();

        var row = await Delivery("handler:note-context");
        var seen = Factory.Services.GetRequiredService<ContextCapturingHandler.Sink>().Seen;
        seen.Count.ShouldBe(1);
        seen[0].DeliveryId.ShouldBe(row.Id);
        seen[0].Attempt.ShouldBe(1);
    }
}
