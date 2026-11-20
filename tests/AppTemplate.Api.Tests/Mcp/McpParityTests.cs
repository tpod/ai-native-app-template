using System.Text.Json;
using System.Text.Json.Nodes;
using AppTemplate.Api.Actions;
using AppTemplate.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Shouldly;

namespace AppTemplate.Api.Tests.Mcp;

/// <summary>The MCP server at <c>/mcp</c> exposes every registered action as tools and runs them through the pipeline.</summary>
public sealed class McpParityTests : ApiTestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListTools_Registry_ExactlyOneEntryPerToolName()
    {
        await using var client = await ConnectAsync();
        var registry = Factory.Services.GetRequiredService<ActionRegistry>();
        registry.All.Count.ShouldBeGreaterThan(0);
        var expected = registry.All
            .SelectMany(d => d.Kind == ActionKind.Mutation ? new[] { $"preview_{d.Name}", d.Name } : new[] { d.Name })
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        var tools = await client.ListToolsAsync(cancellationToken: Ct);

        tools.Select(t => t.Name).OrderBy(x => x, StringComparer.Ordinal).ToList().ShouldBe(expected);
    }

    [Fact]
    public async Task ListTools_ExecuteTool_DescribesErrorsTokenAndConfirmation()
    {
        await using var client = await ConnectAsync();

        var tool = (await client.ListToolsAsync(cancellationToken: Ct)).Single(t => t.Name == "fixture_edit_note").ProtocolTool;

        tool.Description.ShouldNotBeNull();
        tool.Description.ShouldContain("note.locked");
        tool.Description.ShouldContain("validation.failed");
        tool.Description.ShouldContain("previewToken");
        tool.Description.ShouldContain("confirmation");
        var schema = tool.InputSchema;
        schema.GetProperty("type").GetString().ShouldBe("object");
        schema.GetProperty("properties").GetProperty("previewToken").GetProperty("type").GetString().ShouldBe("string");
        schema.GetProperty("properties").TryGetProperty("noteId", out _).ShouldBeTrue();
        schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ShouldContain("previewToken");
    }

    [Fact]
    public async Task ListTools_DefaultedNullableParameter_IsOptionalInSchema()
    {
        await using var client = await ConnectAsync();
        var tools = (await client.ListToolsAsync(cancellationToken: Ct)).ToDictionary(t => t.Name, t => t.ProtocolTool);

        var list = tools["fixture_list_notes"].InputSchema;
        var get = tools["fixture_get_note"].InputSchema;

        list.GetProperty("properties").TryGetProperty("locked", out _).ShouldBeTrue();
        Required(list).ShouldNotContain("locked");
        Required(get).ShouldContain("noteId");
    }

    private static List<string?> Required(JsonElement schema)
        => schema.TryGetProperty("required", out var required) ? [.. required.EnumerateArray().Select(e => e.GetString())] : [];

    [Fact]
    public async Task ListTools_Annotations_MatchKindAndDestructive()
    {
        await using var client = await ConnectAsync();

        var tools = (await client.ListToolsAsync(cancellationToken: Ct)).ToDictionary(t => t.Name, t => t.ProtocolTool);

        var mutations = Factory.Services.GetRequiredService<ActionRegistry>().All.Count(d => d.Kind == ActionKind.Mutation);
        mutations.ShouldBeGreaterThan(0);
        var previews = tools.Where(t => t.Key.StartsWith("preview_", StringComparison.Ordinal)).ToList();
        previews.Count.ShouldBe(mutations);
        previews.ShouldAllBe(t => t.Value.Annotations!.ReadOnlyHint == true);
        tools["fixture_get_note"].Annotations!.ReadOnlyHint.ShouldBe(true);
        tools["fixture_lock_note"].Annotations!.ReadOnlyHint.ShouldBe(false);
        tools["fixture_lock_note"].Annotations!.DestructiveHint.ShouldBe(true);
        tools["fixture_edit_note"].Annotations!.ReadOnlyHint.ShouldBe(false);
        tools["fixture_edit_note"].Annotations!.DestructiveHint.ShouldBe(false);
    }

    [Fact]
    public async Task CallTool_PreviewThenExecute_CreatesNote()
    {
        await using var client = await ConnectAsync();

        var preview = await client.CallToolAsync("preview_fixture_create_note", Args(("text", "hello")), cancellationToken: Ct);
        preview.IsError.ShouldNotBe(true);
        var previewBody = Structured(preview);
        previewBody.GetProperty("summary").GetString().ShouldNotBeNullOrWhiteSpace();
        ShouldBeSameJson(TextOf(preview), previewBody);
        var token = previewBody.GetProperty("previewToken").GetString()!;

        var execute = await client.CallToolAsync("fixture_create_note", Args(("text", "hello"), ("previewToken", token)), cancellationToken: Ct);
        execute.IsError.ShouldNotBe(true);
        var id = Structured(execute).GetProperty("result").GetProperty("id").GetString()!;

        var query = await client.CallToolAsync("fixture_get_note", Args(("noteId", id)), cancellationToken: Ct);
        query.IsError.ShouldNotBe(true);
        Structured(query).GetProperty("text").GetString().ShouldBe("hello");
        TextOf(query).ShouldContain("hello");
    }

    [Fact]
    public async Task CallTool_EverySuccessfulPhase_ReturnsObjectStructuredContent()
    {
        await using var client = await ConnectAsync();
        var token = await PreviewTokenAsync(client, "fixture_create_note", ("text", "hello"));
        var execute = await client.CallToolAsync("fixture_create_note", Args(("text", "hello"), ("previewToken", token)), cancellationToken: Ct);
        var id = Structured(execute).GetProperty("result").GetProperty("id").GetString()!;

        var results = new[]
        {
            execute,
            await client.CallToolAsync("preview_fixture_edit_note", Args(("noteId", id), ("text", "edited")), cancellationToken: Ct),
            await client.CallToolAsync("fixture_get_note", Args(("noteId", id)), cancellationToken: Ct),
            await client.CallToolAsync("fixture_list_notes", Args(), cancellationToken: Ct),
        };

        results.Length.ShouldBe(4);
        foreach (var result in results)
        {
            result.IsError.ShouldNotBe(true, TextOf(result));
            Structured(result).ValueKind.ShouldBe(JsonValueKind.Object, TextOf(result));
            ShouldBeSameJson(TextOf(result), Structured(result));
        }
    }

    [Fact]
    public async Task CallTool_ListQuery_WrapsArrayUnderItems()
    {
        await using var client = await ConnectAsync();
        await CreateNoteAsync(client, "alpha");
        await CreateNoteAsync(client, "beta");

        var result = await client.CallToolAsync("fixture_list_notes", Args(), cancellationToken: Ct);

        result.IsError.ShouldNotBe(true, TextOf(result));
        var items = Structured(result).GetProperty("items");
        items.ValueKind.ShouldBe(JsonValueKind.Array);
        items.EnumerateArray().Select(n => n.GetProperty("text").GetString()).ShouldBe(["alpha", "beta"]);
        var tool = (await client.ListToolsAsync(cancellationToken: Ct)).Single(t => t.Name == "fixture_list_notes");
        tool.Description.ShouldContain("structuredContent.items");
    }

    [Fact]
    public async Task Initialize_Server_InstructionsExplainListResultsUnderItems()
    {
        await using var client = await ConnectAsync();

        client.ServerInstructions.ShouldNotBeNull();
        client.ServerInstructions.ShouldContain("\"items\"");
    }

    [Fact]
    public async Task CallTool_StaleToken_ReturnsIsErrorWithProblemJson()
    {
        await using var client = await ConnectAsync();
        var id = await CreateNoteAsync(client, "hello");
        var stale = await PreviewTokenAsync(client, "fixture_edit_note", ("noteId", id), ("text", "first"));
        var fresh = await PreviewTokenAsync(client, "fixture_edit_note", ("noteId", id), ("text", "second"));
        (await client.CallToolAsync("fixture_edit_note", Args(("noteId", id), ("text", "second"), ("previewToken", fresh)), cancellationToken: Ct))
            .IsError.ShouldNotBe(true);

        var result = await client.CallToolAsync("fixture_edit_note", Args(("noteId", id), ("text", "first"), ("previewToken", stale)), cancellationToken: Ct);

        result.IsError.ShouldBe(true);
        var problem = Structured(result);
        problem.GetProperty("code").GetString().ShouldBe("preview.stale");
        problem.GetProperty("status").GetInt32().ShouldBe(409);
        problem.GetProperty("instance").GetString().ShouldBe("mcp:fixture_edit_note");
        problem.GetProperty("suggestion").GetString().ShouldNotBeNullOrWhiteSpace();
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        ShouldBeSameJson(TextOf(result), problem);
    }

    [Fact]
    public async Task Initialize_Server_ReturnsInstructionsMentioningPreview()
    {
        await using var client = await ConnectAsync();

        client.ServerInfo.ShouldNotBeNull();
        client.ServerInfo.Name.ShouldBe("AppTemplate");
        client.ServerInstructions.ShouldNotBeNull();
        client.ServerInstructions.ShouldContain("preview");
        client.ServerInstructions.ShouldContain("previewToken");
        client.ServerInstructions.ShouldContain("preview.stale");
        client.ServerInstructions.ShouldContain("traceId");
    }

    [Fact]
    public async Task CallTool_UnknownTool_ReturnsNotFoundWithToolsListSuggestion()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("no_such_tool", Args(), cancellationToken: Ct);

        result.IsError.ShouldBe(true);
        var problem = Structured(result);
        problem.GetProperty("code").GetString().ShouldBe("request.not_found");
        problem.GetProperty("suggestion").GetString().ShouldBe("Call tools/list to see available tools.");
        problem.GetProperty("instance").GetString().ShouldBe("mcp:no_such_tool");
    }

    [Fact]
    public async Task CallTool_PreviewOfQuery_ReturnsNotFound()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("preview_fixture_get_note", Args(("noteId", Guid.NewGuid().ToString())), cancellationToken: Ct);

        result.IsError.ShouldBe(true);
        Structured(result).GetProperty("code").GetString().ShouldBe("request.not_found");
    }

    [Fact]
    public async Task CallTool_HandlerThrows_ReturnsUnexpectedWithoutExceptionText()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("preview_fixture_explode", Args(), cancellationToken: Ct);

        result.IsError.ShouldBe(true);
        var problem = Structured(result);
        problem.GetProperty("code").GetString().ShouldBe("internal.unexpected");
        problem.GetProperty("instance").GetString().ShouldBe("mcp:preview_fixture_explode");
        problem.GetProperty("status").GetInt32().ShouldBe(500);
        problem.GetProperty("traceId").GetString().ShouldNotBeNullOrWhiteSpace();
        TextOf(result).ShouldNotContain("boom-secret");
    }

    [Fact]
    public async Task CallTool_InvalidInput_ReturnsValidationProblem()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("preview_fixture_create_note", Args(("text", "")), cancellationToken: Ct);

        result.IsError.ShouldBe(true);
        Structured(result).GetProperty("code").GetString().ShouldBe("validation.failed");
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

    private static async Task<string> CreateNoteAsync(McpClient client, string text)
    {
        var token = await PreviewTokenAsync(client, "fixture_create_note", ("text", text));
        var execute = await client.CallToolAsync("fixture_create_note", Args(("text", text), ("previewToken", token)), cancellationToken: Ct);
        execute.IsError.ShouldNotBe(true, TextOf(execute));
        return Structured(execute).GetProperty("result").GetProperty("id").GetString()!;
    }

    private static async Task<string> PreviewTokenAsync(McpClient client, string action, params (string Key, object? Value)[] args)
    {
        var preview = await client.CallToolAsync($"preview_{action}", Args(args), cancellationToken: Ct);
        preview.IsError.ShouldNotBe(true, TextOf(preview));
        return Structured(preview).GetProperty("previewToken").GetString()!;
    }

    private static Dictionary<string, object?> Args(params (string Key, object? Value)[] args)
        => args.ToDictionary(a => a.Key, a => a.Value);

    private static JsonElement Structured(CallToolResult result)
        => result.StructuredContent ?? throw new ShouldAssertException($"No structured content in {TextOf(result)}");

    /// <summary>The text block carries the same JSON as the structured content (string escaping may differ).</summary>
    private static void ShouldBeSameJson(string text, JsonElement structured)
        => JsonNode.DeepEquals(JsonNode.Parse(text), JsonNode.Parse(structured.GetRawText()))
            .ShouldBeTrue($"text block {text} differs from structured content {structured.GetRawText()}");

    private static string TextOf(CallToolResult result)
        => result.Content.OfType<TextContentBlock>().Single().Text;
}
