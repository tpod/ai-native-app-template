using AppTemplate.Api.Actions.Outbox;
using AppTemplate.Api.Data;
using AppTemplate.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.PostgresTests;

/// <summary>
/// The claim on real Postgres: concurrent claimers never share a row (FOR UPDATE SKIP LOCKED), a later row of the
/// same aggregate waits for the earlier one, and an expired lease is reclaimed.
/// </summary>
public sealed class OutboxClaimTests(PostgresContainerFixture postgres) : PostgresTestBase(postgres)
{
    private const string Key = "handler:project-recorder";

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddEventHandler<ProjectRecorder>(ServiceLifetime.Singleton);

    [Fact]
    public async Task Execute_ThenDrain_DeliversOnPostgres()
    {
        await PreviewAndExecute("create_project", new { name = "Alpha" });

        await DrainOutboxAsync();

        Services.GetRequiredService<ProjectRecorder>().Received.OfType<ProjectCreated>().Single().Name.ShouldBe("Alpha");
        await using var db = NewContext();
        (await db.OutboxDeliveries.SingleAsync(Ct)).Status.ShouldBe(OutboxStatus.Done);
    }

    [Fact]
    public async Task ClaimAsync_TwoConcurrentClaimers_NeverClaimSameRow()
    {
        for (var i = 0; i < 40; i++)
        {
            await PreviewAndExecute("create_project", new { name = $"P{i:00}" });
        }

        List<long> claimed = [];
        for (var round = 0; round < 20; round++)
        {
            var batches = await Task.WhenAll(Claim(5), Claim(5));
            claimed.AddRange(batches.SelectMany(b => b));
        }

        claimed.Count.ShouldBe(40);
        claimed.Distinct().Count().ShouldBe(40);
    }

    [Fact]
    public async Task ClaimAsync_RowsLockedByOpenClaim_SkipsThemWithoutWaiting()
    {
        for (var i = 0; i < 10; i++)
        {
            await PreviewAndExecute("create_project", new { name = $"P{i:00}" });
        }

        await using var holder = Services.CreateAsyncScope();
        var holderDb = holder.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await holderDb.Database.BeginTransactionAsync(Ct);
        var now = DateTimeOffset.UtcNow;
        var held = await holder.ServiceProvider.GetRequiredService<OutboxStore>().ClaimAsync(5, now, now.AddMinutes(5), Ct);

        var other = await Claim(5).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        held.Count.ShouldBe(5);
        other.Count.ShouldBe(5);
        other.Intersect(held).ShouldBeEmpty();
        await transaction.CommitAsync(Ct);
    }

    [Fact]
    public async Task ClaimAsync_SameAggregateRows_ClaimsOnlyEarliest()
    {
        var created = await PreviewAndExecute("create_project", new { name = "Alpha" });
        var id = created.GetProperty("result").GetProperty("id").GetGuid();
        await PreviewAndExecute("rename_project", new { projectId = id, name = "Beta" });
        await PreviewAndExecute("rename_project", new { projectId = id, name = "Gamma" });

        var ids = await Claim(10);

        await using var db = NewContext();
        var first = await db.OutboxDeliveries.Where(d => d.Subscriber == Key).OrderBy(d => d.Id).FirstAsync(Ct);
        ids.ShouldBe([first.Id]);
    }

    [Fact]
    public async Task ClaimAsync_LeaseExpired_ReclaimsRow()
    {
        await PreviewAndExecute("create_project", new { name = "Alpha" });
        var now = DateTimeOffset.UtcNow;
        var first = await Claim(10, leaseUntil: now.AddSeconds(-1));

        var second = await Claim(10);

        second.ShouldBe(first);
        await using var db = NewContext();
        (await db.OutboxDeliveries.SingleAsync(Ct)).Attempts.ShouldBe(2);
    }

    /// <summary>One claim on its own scope and connection, as a separate worker would make it.</summary>
    private async Task<IReadOnlyList<long>> Claim(int batchSize, DateTimeOffset? leaseUntil = null)
    {
        await using var scope = Services.CreateAsyncScope();
        var now = DateTimeOffset.UtcNow;
        return await scope.ServiceProvider.GetRequiredService<OutboxStore>()
            .ClaimAsync(batchSize, now, leaseUntil ?? now.AddMinutes(5), Ct);
    }
}
