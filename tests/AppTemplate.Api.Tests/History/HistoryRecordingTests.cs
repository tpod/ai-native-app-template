using System.Net;
using System.Text.Json;
using AppTemplate.Api.Actions;
using AppTemplate.Api.Actions.Preview;
using AppTemplate.Api.Data;
using AppTemplate.Api.Tests.Fixtures.Notes;
using AppTemplate.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using Shouldly;

namespace AppTemplate.Api.Tests.History;

/// <summary>Every committed execute stores the events it raised as history rows, in the same transaction.</summary>
public sealed class HistoryRecordingTests : ApiTestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected override void ConfigureServices(IServiceCollection services)
        => services.Configure<ActionRegistryOptions>(o => o.Types.AddRange(SilentNoteFixtures.Types));

    [Fact]
    public async Task Execute_CreateNote_WritesOneRowWithEveryColumn()
    {
        var (p, e) = await PreviewAndExecute("fixture_create_note", new { text = "hello" });
        e.ShouldSucceed();

        var row = (await Rows()).ShouldHaveSingleItem();
        row.EventType.ShouldBe("note.created");
        row.EventVersion.ShouldBe(1);
        row.AggregateType.ShouldBe("Note");
        row.AggregateId.ShouldBe(IdFromChanges(p));
        row.AggregateVersion.ShouldBe(1);
        row.Action.ShouldBe("fixture_create_note");
        row.Channel.ShouldBe("http");
        row.Reason.ShouldBeNull();
        JsonDocument.Parse(row.Payload).RootElement.GetProperty("text").GetString().ShouldBe("hello");
        row.OccurredAt.ShouldBe(Factory.Time.GetUtcNow());
        row.ExecutionId.ShouldNotBe(Guid.Empty);
        row.Id.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Execute_EditNote_AppendsSecondRowWithHigherVersion()
    {
        var id = await CreateNote("hello");

        (await PreviewAndExecute("fixture_edit_note", new { noteId = id, text = "edited" })).execute.ShouldSucceed();

        var rows = await Rows();
        rows.Count.ShouldBe(2);
        rows[1].EventType.ShouldBe("note.edited");
        rows[1].AggregateVersion.ShouldBe(2);
        rows[1].Id.ShouldBeGreaterThan(rows[0].Id);
        rows[1].ExecutionId.ShouldNotBe(rows[0].ExecutionId);
    }

    [Fact]
    public async Task Preview_Any_WritesNoRows()
    {
        (await Preview("fixture_create_note", new { text = "hello" })).ShouldSucceed();

        (await Rows()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Execute_Stale_WritesNoRows()
    {
        var id = await CreateNote("hello");
        var a = await Preview("fixture_edit_note", new { noteId = id, text = "A" });
        (await PreviewAndExecute("fixture_edit_note", new { noteId = id, text = "B" })).execute.ShouldSucceed();
        (await Rows()).Count.ShouldBe(2);

        (await Execute("fixture_edit_note", new { noteId = id, text = "A" }, TokenOf(a))).ShouldFailWith("preview.stale", HttpStatusCode.Conflict);

        (await Rows()).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Execute_AlreadyExecutedCreate_WritesNoDuplicateRow()
    {
        var p = await Preview("fixture_create_note", new { text = "hello" });
        (await Execute("fixture_create_note", new { text = "hello" }, TokenOf(p))).ShouldSucceed();

        (await Execute("fixture_create_note", new { text = "hello" }, TokenOf(p))).ShouldFailWith("preview.already_executed");

        (await Rows()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Execute_LockNote_RecordsNoteLocked()
    {
        var id = await CreateNote("hello");

        (await PreviewAndExecute("fixture_lock_note", new { noteId = id })).execute.ShouldSucceed();

        (await Rows())[^1].EventType.ShouldBe("note.locked");
    }

    [Fact]
    public async Task Preview_StateChangeWithoutEvent_FailsUnexpected()
    {
        var id = await CreateNote("hello");

        (await Preview("fixture_touch_note_silently", new { noteId = id, text = "changed" }))
            .ShouldFailWith("internal.unexpected", HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task Execute_StateChangeWithoutEvent_FailsUnexpected()
    {
        var id = await CreateNote("hello");
        var input = new SilentNoteInput(Guid.Parse(id), "changed");

        var e = await Execute("fixture_touch_note_silently", new { noteId = id, text = "changed" }, SignedToken("fixture_touch_note_silently", input, id));

        e.ShouldFailWith("internal.unexpected", HttpStatusCode.InternalServerError);
        (await NoteText(id)).ShouldBe("hello");
        (await Rows()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Execute_EventWithoutAttribute_FailsUnexpected()
    {
        var id = await CreateNote("hello");
        var input = new SilentNoteInput(Guid.Parse(id), "changed");

        var e = await Execute("fixture_edit_note_unnamed", new { noteId = id, text = "changed" }, SignedToken("fixture_edit_note_unnamed", input, id));

        e.ShouldFailWith("internal.unexpected", HttpStatusCode.InternalServerError);
        (await NoteText(id)).ShouldBe("hello");
        (await Rows()).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Execute_McpCall_RecordsMcpChannel()
    {
        await using var client = await ConnectAsync();
        var args = new Dictionary<string, object?> { ["text"] = "hello" };
        var preview = await client.CallToolAsync("preview_fixture_create_note", args, cancellationToken: Ct);
        preview.IsError.ShouldNotBe(true);
        args["previewToken"] = preview.StructuredContent!.Value.GetProperty("previewToken").GetString();

        (await client.CallToolAsync("fixture_create_note", args, cancellationToken: Ct)).IsError.ShouldNotBe(true);

        (await Rows()).ShouldHaveSingleItem().Channel.ShouldBe("mcp");
    }

    private async Task<List<HistoryEntry>> Rows()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().History.OrderBy(h => h.Id).ToListAsync(Ct);
    }

    private async Task<string> CreateNote(string text)
    {
        var (p, e) = await PreviewAndExecute("fixture_create_note", new { text });
        e.ShouldSucceed();
        return IdFromChanges(p);
    }

    private async Task<string?> NoteText(string id)
    {
        var q = await Query("fixture_get_note", new { noteId = id });
        q.ShouldSucceed();
        return q.Body.GetProperty("text").GetString();
    }

    /// <summary>The preview of these mutations fails by design, so sign a token for the note at version 1 directly.</summary>
    private string SignedToken(string action, SilentNoteInput input, string noteId)
    {
        var signer = Factory.Services.GetRequiredService<PreviewTokenSigner>();
        var json = Factory.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        return signer.Sign(new PreviewTokenPayload(
            action,
            InputHasher.Hash(input, typeof(SilentNoteInput), json),
            [new AggregateVersion("Note", noteId, 1)],
            "unused",
            [],
            Factory.Time.GetUtcNow().AddSeconds(300)));
    }

    private Task<McpClient> ConnectAsync()
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(Client.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            Client,
            loggerFactory: null,
            ownsHttpClient: false);
        return McpClient.CreateAsync(transport, cancellationToken: Ct);
    }
}
