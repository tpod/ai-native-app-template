using AppTemplate.Api.Data;
using AppTemplate.Api.Tests.Fixtures.Notes;
using AppTemplate.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.Tests.Outbox;

/// <summary>Notes-fixture helpers for outbox tests: create and edit notes, read delivery rows.</summary>
public abstract class OutboxTestBase : ApiTestBase
{
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected RecordingEventHandler Recorder => Factory.Services.GetRequiredService<RecordingEventHandler>();

    protected async Task<string> CreateNote(string text)
    {
        var (p, e) = await PreviewAndExecute("fixture_create_note", new { text });
        e.ShouldSucceed();
        return IdFromChanges(p);
    }

    protected async Task EditNote(string id, string text)
    {
        var (_, e) = await PreviewAndExecute("fixture_edit_note", new { noteId = id, text });
        e.ShouldSucceed();
    }

    /// <summary>Every delivery row with its history entry, oldest first.</summary>
    protected async Task<List<OutboxDelivery>> Deliveries()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxDeliveries
            .AsNoTracking()
            .Include(d => d.HistoryEntry)
            .OrderBy(d => d.Id)
            .ToListAsync(Ct);
    }

    /// <summary>The <paramref name="index"/>-th delivery row (oldest first) for one subscriber key.</summary>
    protected async Task<OutboxDelivery> Delivery(string subscriber, int index = 0)
    {
        var rows = (await Deliveries()).Where(d => d.Subscriber == subscriber).ToList();
        rows.Count.ShouldBeGreaterThan(index, $"No delivery #{index} for {subscriber}");
        return rows[index];
    }
}
