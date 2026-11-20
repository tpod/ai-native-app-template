using System.Net;
using System.Text;
using System.Text.Json;
using AppTemplate.Api.Actions;
using AppTemplate.Api.Actions.Preview;
using AppTemplate.Api.Tests.Fixtures.Notes;
using AppTemplate.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace AppTemplate.Api.Tests.Actions;

public sealed class PipelineTests : ApiTestBase
{
    [Fact]
    public async Task Preview_CreateNote_ReturnsChangesSummaryTokenAndNoCommit()
    {
        var p = await Preview("fixture_create_note", new { text = "hello" });

        p.ShouldSucceed();
        p.Body.GetProperty("action").GetString().ShouldBe("fixture_create_note");
        p.Body.GetProperty("summary").GetString().ShouldNotBeNullOrWhiteSpace();
        p.Body.GetProperty("changes").EnumerateArray().ShouldContain(c => c.GetProperty("field").GetString() == "text");
        p.Body.GetProperty("sideEffects")[0].GetProperty("event").GetString().ShouldBe("NoteCreated");
        TokenOf(p).ShouldNotBeNullOrWhiteSpace();
        p.Body.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(Factory.Time.GetUtcNow().AddSeconds(300));
        (await Query("fixture_get_note", new { noteId = IdFromChanges(p) })).ShouldFailWith("note.not_found", HttpStatusCode.NotFound);
        Factory.Services.GetRequiredService<RecordingEventHandler>().Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task Execute_ValidToken_CommitsReturnsResultAndDispatchesEvent()
    {
        var (p, e) = await PreviewAndExecute("fixture_create_note", new { text = "hello" });

        e.ShouldSucceed();
        var id = IdFromChanges(p);
        e.Body.GetProperty("action").GetString().ShouldBe("fixture_create_note");
        e.Body.GetProperty("result").GetProperty("id").GetString().ShouldBe(id);
        e.Body.GetProperty("result").GetProperty("text").GetString().ShouldBe("hello");
        var changes = e.Body.GetProperty("changes").EnumerateArray().ToList();
        changes.ShouldContain(c => c.GetProperty("field").GetString() == "text" && c.GetProperty("op").GetString() == "added");

        var q = await Query("fixture_get_note", new { noteId = id });
        q.ShouldSucceed();
        q.Body.GetProperty("text").GetString().ShouldBe("hello");

        var received = Factory.Services.GetRequiredService<RecordingEventHandler>().Received;
        received.Count.ShouldBe(1);
        received[0].ShouldBeOfType<NoteCreated>().NoteId.ShouldBe(Guid.Parse(id));
    }

    [Fact]
    public async Task Execute_SameUpdateTokenTwice_SecondIsStale()
    {
        var id = await CreateNote("hello");
        var p = await Preview("fixture_edit_note", new { noteId = id, text = "world" });

        (await Execute("fixture_edit_note", new { noteId = id, text = "world" }, TokenOf(p))).ShouldSucceed();
        var second = await Execute("fixture_edit_note", new { noteId = id, text = "world" }, TokenOf(p));

        second.ShouldFailWith("preview.stale", HttpStatusCode.Conflict);
        second.Body.GetProperty("details").GetProperty("reason").GetString().ShouldBe("version_changed");
        Factory.Services.GetRequiredService<RecordingEventHandler>().Received.OfType<NoteEdited>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Execute_SameCreateTokenTwice_SecondIsAlreadyExecuted()
    {
        var p = await Preview("fixture_create_note", new { text = "hello" });

        (await Execute("fixture_create_note", new { text = "hello" }, TokenOf(p))).ShouldSucceed();
        var second = await Execute("fixture_create_note", new { text = "hello" }, TokenOf(p));

        second.ShouldFailWith("preview.already_executed", HttpStatusCode.Conflict);
        Factory.Services.GetRequiredService<RecordingEventHandler>().Received.OfType<NoteCreated>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Execute_TamperedToken_Returns400TokenInvalid()
    {
        var p = await Preview("fixture_create_note", new { text = "hello" });
        var token = TokenOf(p);
        var tampered = (token[0] == 'A' ? 'B' : 'A') + token[1..];

        (await Execute("fixture_create_note", new { text = "hello" }, tampered))
            .ShouldFailWith("preview.token_invalid", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Execute_MissingToken_Returns400TokenInvalid()
    {
        var r = await PostRaw("/api/actions/fixture_create_note", """{"text":"hello"}""");

        r.ShouldFailWith("preview.token_invalid", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Execute_DifferentInput_ReturnsTokenMismatch()
    {
        var id = await CreateNote("hello");
        var p = await Preview("fixture_edit_note", new { noteId = id, text = "world" });

        (await Execute("fixture_edit_note", new { noteId = id, text = "other" }, TokenOf(p)))
            .ShouldFailWith("preview.token_mismatch", HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Execute_TokenFromOtherAction_ReturnsTokenMismatch()
    {
        var id = await CreateNote("hello");
        var p = await Preview("fixture_edit_note", new { noteId = id, text = "world" });

        (await Execute("fixture_lock_note", new { noteId = id }, TokenOf(p)))
            .ShouldFailWith("preview.token_mismatch", HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Execute_After301Seconds_ReturnsExpired()
    {
        var id = await CreateNote("hello");
        var p = await Preview("fixture_edit_note", new { noteId = id, text = "world" });

        Factory.Time.Advance(TimeSpan.FromSeconds(301));
        var e = await Execute("fixture_edit_note", new { noteId = id, text = "world" }, TokenOf(p));

        e.ShouldFailWith("preview.expired", HttpStatusCode.Conflict);
        (await GetNoteText(id)).ShouldBe("hello");
    }

    [Fact]
    public async Task Execute_NoteEditedSincePreview_ReturnsStaleVersionChanged()
    {
        var id = await CreateNote("hello");
        var stale = await Preview("fixture_edit_note", new { noteId = id, text = "first" });
        (await PreviewAndExecute("fixture_edit_note", new { noteId = id, text = "second" })).execute.ShouldSucceed();

        var e = await Execute("fixture_edit_note", new { noteId = id, text = "first" }, TokenOf(stale));

        e.ShouldFailWith("preview.stale", HttpStatusCode.Conflict);
        e.Body.GetProperty("details").GetProperty("reason").GetString().ShouldBe("version_changed");
        (await GetNoteText(id)).ShouldBe("second");
    }

    [Fact]
    public async Task Execute_NoteLockedSincePreview_ReturnsStale()
    {
        var id = await CreateNote("hello");
        var stale = await Preview("fixture_edit_note", new { noteId = id, text = "world" });
        (await PreviewAndExecute("fixture_lock_note", new { noteId = id })).execute.ShouldSucceed();

        var e = await Execute("fixture_edit_note", new { noteId = id, text = "world" }, TokenOf(stale));

        e.ShouldFailWith("preview.stale", HttpStatusCode.Conflict);
        (await GetNoteText(id)).ShouldBe("hello");
    }

    [Fact]
    public async Task Execute_HashDiffersWithSameVersions_ReturnsStaleChangesDiffer()
    {
        // A create has no loaded aggregate to version-check; a different change set must still be caught.
        var signer = Factory.Services.GetRequiredService<PreviewTokenSigner>();
        var json = Factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        var token = signer.Sign(new PreviewTokenPayload(
            "fixture_create_note",
            InputHasher.Hash(new CreateNoteInput("hello"), typeof(CreateNoteInput), json),
            [],
            "not-the-real-hash",
            [Guid.CreateVersion7()],
            Factory.Time.GetUtcNow().AddSeconds(300)));

        var e = await Execute("fixture_create_note", new { text = "hello" }, token);

        e.ShouldFailWith("preview.stale", HttpStatusCode.Conflict);
        e.Body.GetProperty("details").GetProperty("reason").GetString().ShouldBe("changes_differ");
    }

    [Fact]
    public async Task PreviewAndExecute_UnchangedText_EmptyChangesWarningAndVersionKept()
    {
        var id = await CreateNote("hello");
        var versionBefore = (await Query("fixture_get_note", new { noteId = id })).Body.GetProperty("version").GetInt32();
        var other = await Preview("fixture_edit_note", new { noteId = id, text = "world" });

        var (p, e) = await PreviewAndExecute("fixture_edit_note", new { noteId = id, text = "hello" });

        p.ShouldSucceed();
        p.Body.GetProperty("warnings")[0].GetProperty("code").GetString().ShouldBe("note.unchanged");
        p.Body.GetProperty("changes").GetArrayLength().ShouldBe(0);
        e.ShouldSucceed();
        e.Body.GetProperty("changes").GetArrayLength().ShouldBe(0);
        (await Query("fixture_get_note", new { noteId = id })).Body.GetProperty("version").GetInt32().ShouldBe(versionBefore);
        (await Execute("fixture_edit_note", new { noteId = id, text = "world" }, TokenOf(other))).ShouldSucceed();
        (await GetNoteText(id)).ShouldBe("world");
    }

    [Fact]
    public async Task Preview_InvalidText_Returns422WithFieldErrors()
    {
        (await Preview("fixture_create_note", new { text = "" })).ShouldHaveFieldError("text", "note_text.empty");
    }

    [Theory]
    [InlineData("{\"noteId\":\"not-a-guid\",\"text\":\"x\"}", "$.noteId")]
    [InlineData("[]", "$")]
    [InlineData("", "$")]
    [InlineData("{\"noteId\":", "$")]
    public async Task Preview_MalformedBody_Returns400RequestMalformed(string raw, string path)
    {
        var r = await PostRaw("/api/actions/fixture_edit_note/preview", raw);

        r.ShouldFailWith("request.malformed", HttpStatusCode.BadRequest);
        r.Body.GetProperty("details").GetProperty("path").GetString().ShouldBe(path);
    }

    [Fact]
    public async Task Preview_MissingRequiredField_Returns400WithFieldPath()
    {
        var r = await PostRaw("/api/actions/fixture_edit_note/preview", "{\"noteId\":\"" + Guid.NewGuid() + "\"}");

        r.ShouldFailWith("request.malformed", HttpStatusCode.BadRequest);
        r.Body.GetProperty("details").GetProperty("path").GetString().ShouldBe("$.text");
        r.Body.GetProperty("detail").GetString()!.ShouldContain("'text'");
    }

    [Fact]
    public async Task Preview_MissingGuidField_Returns400NotEmptyGuid()
    {
        var r = await PostRaw("/api/actions/fixture_untracked_edit/preview", "{\"text\":\"x\"}");

        r.ShouldFailWith("request.malformed", HttpStatusCode.BadRequest);
        r.Body.GetProperty("details").GetProperty("path").GetString().ShouldBe("$.noteId");
    }

    [Fact]
    public async Task Preview_NullForNonNullableField_Returns400WithFieldPath()
    {
        var r = await PostRaw("/api/actions/fixture_create_note/preview", "{\"text\":null}");

        r.ShouldFailWith("request.malformed", HttpStatusCode.BadRequest);
        r.Body.GetProperty("details").GetProperty("path").GetString().ShouldBe("$.text");
    }

    [Fact]
    public async Task Query_OptionalFieldOmitted_Succeeds()
    {
        await CreateNote("hello");

        var q = await Query("fixture_list_notes", new { });

        q.ShouldSucceed();
        q.Body.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task PreviewAndExecute_HandlerThrows_Returns500WithoutExceptionText()
    {
        var p = await Preview("fixture_explode", new { });
        p.ShouldFailWith("internal.unexpected", HttpStatusCode.InternalServerError);
        p.Body.GetRawText().ShouldNotContain("boom-secret");

        // Preview never succeeds, so sign a token directly to drive execute into the throwing Apply.
        var signer = Factory.Services.GetRequiredService<PreviewTokenSigner>();
        var json = Factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        var token = signer.Sign(new PreviewTokenPayload(
            "fixture_explode",
            InputHasher.Hash(new ExplodeInput(), typeof(ExplodeInput), json),
            [],
            "unused",
            [Guid.CreateVersion7()],
            Factory.Time.GetUtcNow().AddSeconds(300)));
        var e = await Execute("fixture_explode", new { }, token);

        e.ShouldFailWith("internal.unexpected", HttpStatusCode.InternalServerError);
        e.Body.GetRawText().ShouldNotContain("boom-secret");
    }

    [Fact]
    public async Task Preview_LoadReturnsUntrackedAggregate_Returns500NotSilentNoOp()
    {
        var id = await CreateNote("hello");

        var p = await Preview("fixture_untracked_edit", new { noteId = id, text = "edited" });

        p.ShouldFailWith("internal.unexpected", HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task PreviewAndExecute_LoadReturnsUntrackedAggregate_ThrowsNamingHandler()
    {
        var id = await CreateNote("hello");
        var registry = Factory.Services.GetRequiredService<ActionRegistry>();
        var pipeline = Factory.Services.GetRequiredService<ActionPipeline>();
        var action = registry.Find("fixture_untracked_edit")!;
        var json = Factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        var input = new UntrackedEditNoteInput(Guid.Parse(id), "edited");
        var token = Factory.Services.GetRequiredService<PreviewTokenSigner>().Sign(new PreviewTokenPayload(
            "fixture_untracked_edit",
            InputHasher.Hash(input, typeof(UntrackedEditNoteInput), json),
            [],
            "unused",
            [],
            Factory.Time.GetUtcNow().AddSeconds(300)));
        var previewBody = JsonSerializer.SerializeToElement(new { noteId = id, text = "edited" });
        var executeBody = JsonSerializer.SerializeToElement(new { noteId = id, text = "edited", previewToken = token });

        var preview = await Should.ThrowAsync<InvalidOperationException>(
            () => pipeline.PreviewAsync(action, previewBody, TestContext.Current.CancellationToken));
        var execute = await Should.ThrowAsync<InvalidOperationException>(
            () => pipeline.ExecuteAsync(action, executeBody, ActionCaller.Http, TestContext.Current.CancellationToken));

        foreach (var e in new[] { preview, execute })
        {
            e.Message.ShouldContain(nameof(UntrackedEditNote));
            e.Message.ShouldContain("Load must return a tracked aggregate (no AsNoTracking)");
        }

        (await GetNoteText(id)).ShouldBe("hello");
    }

    [Fact]
    public async Task Query_ExistingNote_ReturnsDto()
    {
        var id = await CreateNote("hello");

        var q = await Query("fixture_get_note", new { noteId = id });

        q.ShouldSucceed();
        q.Body.GetProperty("id").GetString().ShouldBe(id);
        q.Body.GetProperty("text").GetString().ShouldBe("hello");
        q.Body.GetProperty("locked").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Query_UnknownNote_Returns404NotFound()
    {
        (await Query("fixture_get_note", new { noteId = Guid.NewGuid() })).ShouldFailWith("note.not_found", HttpStatusCode.NotFound);
    }

    private async Task<string> CreateNote(string text)
    {
        var (p, e) = await PreviewAndExecute("fixture_create_note", new { text });
        e.ShouldSucceed();
        return IdFromChanges(p);
    }

    private async Task<string?> GetNoteText(string id)
    {
        var q = await Query("fixture_get_note", new { noteId = id });
        q.ShouldSucceed();
        return q.Body.GetProperty("text").GetString();
    }

    private async Task<ApiResponse> PostRaw(string path, string raw)
    {
        using var content = new StringContent(raw, Encoding.UTF8, "application/json");
        return await ApiResponse.From(await Client.PostAsync(path, content, TestContext.Current.CancellationToken));
    }
}
