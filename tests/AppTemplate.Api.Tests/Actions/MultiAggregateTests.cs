using System.Net;
using AppTemplate.Api.Actions;
using AppTemplate.Api.Data;
using AppTemplate.Api.Tests.Fixtures.Notes;
using AppTemplate.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.Tests.Actions;

/// <summary>An execute that changes several aggregates treats each one as it treats a single target.</summary>
public sealed class MultiAggregateTests : ApiTestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected override void ConfigureServices(IServiceCollection services)
        => services.Configure<ActionRegistryOptions>(o => o.Types.AddRange(MultiNoteFixtures.Types));

    [Fact]
    public async Task Execute_EditTwoNotes_BumpsBothVersions()
    {
        var first = await CreateNote("one");
        var second = await CreateNote("two");

        (await PreviewAndExecute("fixture_edit_two_notes", new { noteId = first, otherNoteId = second, text = "both" }))
            .execute.ShouldSucceed();

        (await NoteVersion(first)).ShouldBe(2);
        (await NoteVersion(second)).ShouldBe(2);
    }

    [Fact]
    public async Task Execute_EditTwoNotes_RecordsEachNotesNewVersionInHistory()
    {
        var first = await CreateNote("one");
        var second = await CreateNote("two");

        (await PreviewAndExecute("fixture_edit_two_notes", new { noteId = first, otherNoteId = second, text = "both" }))
            .execute.ShouldSucceed();

        var edits = (await Rows()).Where(r => r.EventType == "note.edited").ToList();
        edits.Count.ShouldBe(2);
        edits.Select(r => r.AggregateId).ShouldBe([first, second], ignoreOrder: true);
        edits.ShouldAllBe(r => r.AggregateVersion == 2);
        edits.Select(r => r.ExecutionId).Distinct().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Execute_SecondNoteEditedSincePreview_ReturnsStaleVersionChanged()
    {
        var first = await CreateNote("one");
        var second = await CreateNote("two");
        var p = await Preview("fixture_edit_two_notes", new { noteId = first, otherNoteId = second, text = "both" });
        p.ShouldSucceed();
        (await PreviewAndExecute("fixture_edit_note", new { noteId = second, text = "meanwhile" })).execute.ShouldSucceed();

        var e = await Execute("fixture_edit_two_notes", new { noteId = first, otherNoteId = second, text = "both" }, TokenOf(p));

        e.ShouldFailWith("preview.stale", HttpStatusCode.Conflict);
        e.Body.GetProperty("details").GetProperty("reason").GetString().ShouldBe("version_changed");
    }

    [Fact]
    public async Task Preview_SecondNoteChangedWithoutEvent_FailsUnexpected()
    {
        var first = await CreateNote("one");
        var second = await CreateNote("two");

        (await Preview("fixture_edit_note_touch_other", new { noteId = first, otherNoteId = second, text = "both" }))
            .ShouldFailWith("internal.unexpected", HttpStatusCode.InternalServerError);
    }

    private async Task<string> CreateNote(string text)
    {
        var (p, e) = await PreviewAndExecute("fixture_create_note", new { text });
        e.ShouldSucceed();
        return IdFromChanges(p);
    }

    private async Task<int> NoteVersion(string id)
    {
        var q = await Query("fixture_get_note", new { noteId = id });
        q.ShouldSucceed();
        return q.Body.GetProperty("version").GetInt32();
    }

    private async Task<List<HistoryEntry>> Rows()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().History.OrderBy(h => h.Id).ToListAsync(Ct);
    }
}
