using System.Net;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;

namespace AppTemplate.Api.Tests.Features.History;

/// <summary>
/// get_history is generic over every aggregate, so it is tested on the Notes fixture: these tests survive
/// skill:remove-example. The Projects read-back lives in RenameProjectTests.
/// </summary>
public sealed class GetHistoryTests : ApiTestBase
{
    [Fact]
    [Covers("get_history", Case.HappyPath)]
    public async Task Query_EditedNote_ReturnsCreatedThenEdited()
    {
        var id = await CreateNote("hello");
        (await PreviewAndExecute("fixture_edit_note", new { noteId = id, text = "edited" })).execute.ShouldSucceed();

        var q = await Query("get_history", new { aggregateType = "Note", aggregateId = id });

        q.ShouldSucceed();
        q.Body.GetArrayLength().ShouldBe(2);
        q.Body[0].GetProperty("eventType").GetString().ShouldBe("note.created");
        q.Body[1].GetProperty("eventType").GetString().ShouldBe("note.edited");
        q.Body[1].GetProperty("payload").GetProperty("newText").GetString().ShouldBe("edited");
        q.Body[1].GetProperty("action").GetString().ShouldBe("fixture_edit_note");
        q.Body[1].GetProperty("channel").GetString().ShouldBe("http");
        q.Body[1].GetProperty("aggregateVersion").GetInt32().ShouldBe(2);
    }

    [Fact]
    [Covers("get_history", "history.unknown_aggregate_type")]
    public async Task Query_UnknownType_ReturnsUnknownAggregateType()
    {
        var q = await Query("get_history", new { aggregateType = "Invoice", aggregateId = Guid.NewGuid().ToString() });

        q.ShouldFailWith("history.unknown_aggregate_type", HttpStatusCode.BadRequest);
        q.Body.GetProperty("details").GetProperty("known").EnumerateArray().Select(k => k.GetString()).ShouldContain("Note");
    }

    [Fact]
    [Covers("get_history", "validation.failed")]
    public async Task Query_LimitZero_ReturnsValidationFailed()
        => (await Query("get_history", new { aggregateType = "Note", aggregateId = Guid.NewGuid().ToString(), limit = 0 }))
            .ShouldHaveFieldError("limit", "history.limit_out_of_range");

    [Fact]
    public async Task Query_EmptyAggregateId_ReturnsValidationFailed()
        => (await Query("get_history", new { aggregateType = "Note", aggregateId = "" }))
            .ShouldHaveFieldError("aggregateId", "history.field_required");

    [Fact]
    public async Task Query_LowercaseType_MatchesNote()
    {
        var id = await CreateNote("hello");

        var q = await Query("get_history", new { aggregateType = "note", aggregateId = id });

        q.ShouldSucceed();
        q.Body.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Query_UppercaseGuid_MatchesRows()
    {
        var id = await CreateNote("hello");

        var q = await Query("get_history", new { aggregateType = "Note", aggregateId = Guid.Parse(id).ToString("B").ToUpperInvariant() });

        q.ShouldSucceed();
        q.Body.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Query_AfterIdPastEnd_ReturnsEmpty()
    {
        var id = await CreateNote("hello");
        var all = await Query("get_history", new { aggregateType = "Note", aggregateId = id });
        var last = all.Body[0].GetProperty("id").GetInt64();

        var q = await Query("get_history", new { aggregateType = "Note", aggregateId = id, afterId = last });

        q.ShouldSucceed();
        q.Body.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Query_Limit1_ReturnsOnlyFirst()
    {
        var id = await CreateNote("hello");
        (await PreviewAndExecute("fixture_edit_note", new { noteId = id, text = "edited" })).execute.ShouldSucceed();

        var q = await Query("get_history", new { aggregateType = "Note", aggregateId = id, limit = 1 });

        q.ShouldSucceed();
        q.Body.GetArrayLength().ShouldBe(1);
        q.Body[0].GetProperty("eventType").GetString().ShouldBe("note.created");
    }

    [Fact]
    public async Task Query_NewestFirstLimit1_ReturnsLatest()
    {
        var id = await CreateNote("hello");
        (await PreviewAndExecute("fixture_edit_note", new { noteId = id, text = "edited" })).execute.ShouldSucceed();

        var q = await Query("get_history", new { aggregateType = "Note", aggregateId = id, newestFirst = true, limit = 1 });

        q.ShouldSucceed();
        q.Body.GetArrayLength().ShouldBe(1);
        q.Body[0].GetProperty("eventType").GetString().ShouldBe("note.edited");
    }

    [Fact]
    public async Task Query_NewestFirstBeforeId_ReturnsOlderPage()
    {
        var id = await CreateNote("hello");
        (await PreviewAndExecute("fixture_edit_note", new { noteId = id, text = "edited" })).execute.ShouldSucceed();
        (await PreviewAndExecute("fixture_edit_note", new { noteId = id, text = "again" })).execute.ShouldSucceed();
        var first = await Query("get_history", new { aggregateType = "Note", aggregateId = id, newestFirst = true, limit = 1 });
        var newest = first.Body[0].GetProperty("id").GetInt64();

        var q = await Query("get_history", new { aggregateType = "Note", aggregateId = id, newestFirst = true, beforeId = newest });

        q.ShouldSucceed();
        q.Body.EnumerateArray().Select(e => e.GetProperty("eventType").GetString()).ShouldBe(["note.edited", "note.created"]);
    }

    [Fact]
    public async Task Query_NoRows_ReturnsEmpty()
    {
        var q = await Query("get_history", new { aggregateType = "Note", aggregateId = Guid.NewGuid().ToString() });

        q.ShouldSucceed();
        q.Body.GetArrayLength().ShouldBe(0);
    }

    private async Task<string> CreateNote(string text)
    {
        var (p, e) = await PreviewAndExecute("fixture_create_note", new { text });
        e.ShouldSucceed();
        return IdFromChanges(p);
    }
}
