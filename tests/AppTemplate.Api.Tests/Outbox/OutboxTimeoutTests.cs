using AppTemplate.Api.Actions.Outbox;
using AppTemplate.Api.Data;
using AppTemplate.Api.Tests.Fixtures.Notes;
using AppTemplate.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.Tests.Outbox;

/// <summary>
/// A handler that hangs is cut off at <see cref="OutboxOptions.HandlerTimeout"/> and retried; a host shutting down
/// mid-delivery records nothing and leaves the row for its lease to expire.
/// </summary>
public sealed class OutboxTimeoutTests : OutboxTestBase
{
    private FlakyNoteHandler Flaky => Factory.Services.GetRequiredService<FlakyNoteHandler>();

    protected override ApiFactory CreateFactory()
    {
        var factory = new ApiFactory();
        factory.Settings["Outbox:HandlerTimeout"] = "00:00:01";
        return factory;
    }

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddEventHandler<FlakyNoteHandler>(ServiceLifetime.Singleton);

    [Fact]
    public async Task Drain_HandlerExceedsTimeout_RecordsFailure()
    {
        Flaky.Hang = true;
        await CreateNote("a");

        await DrainOutboxAsync();

        var row = await Delivery("handler:note-flaky");
        row.Status.ShouldBe(OutboxStatus.Pending);
        row.Attempts.ShouldBe(1);
        row.LastError.ShouldNotBeNull().ShouldContain("timed out");
    }

    [Fact]
    public async Task Deliver_HostCancelled_LeavesRowInFlight()
    {
        Flaky.Hang = true;
        await CreateNote("a");
        using var host = new CancellationTokenSource();
        var run = Factory.Services.GetRequiredService<OutboxProcessor>().RunOnceAsync(host.Token);

        await Flaky.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        await host.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => run);
        var row = await Delivery("handler:note-flaky");
        row.Status.ShouldBe(OutboxStatus.InFlight);
        row.LastError.ShouldBeNull();
    }
}
