using System.Net;
using System.Text.Json.Nodes;
using AppTemplate.Api.Data;
using AppTemplate.Api.Tests.Infrastructure;
using AppTemplate.Api.Tests.OpenApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Shouldly;

namespace AppTemplate.Api.Tests.History;

/// <summary>An execute may carry an optional <c>reason</c>, stored on its history rows and never bound by the token.</summary>
public sealed class ReasonTests : ApiTestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Execute_WithReason_StoresTrimmedReason()
    {
        var p = await Preview("fixture_create_note", new { text = "hello" });

        (await Execute("fixture_create_note", new { text = "hello" }, TokenOf(p), "  customer asked  ")).ShouldSucceed();

        (await Rows()).ShouldHaveSingleItem().Reason.ShouldBe("customer asked");
    }

    [Fact]
    public async Task Execute_WhitespaceReason_StoresNull()
    {
        var p = await Preview("fixture_create_note", new { text = "hello" });

        (await Execute("fixture_create_note", new { text = "hello" }, TokenOf(p), "   ")).ShouldSucceed();

        (await Rows()).ShouldHaveSingleItem().Reason.ShouldBeNull();
    }

    [Fact]
    public async Task Execute_NullReason_StoresNull()
    {
        var p = await Preview("fixture_create_note", new { text = "hello" });

        (await Execute("fixture_create_note", new { text = "hello" }, TokenOf(p), null)).ShouldSucceed();

        (await Rows()).ShouldHaveSingleItem().Reason.ShouldBeNull();
    }

    [Fact]
    public async Task Execute_Reason500Chars_Succeeds()
    {
        var p = await Preview("fixture_create_note", new { text = "hello" });

        (await Execute("fixture_create_note", new { text = "hello" }, TokenOf(p), new string('r', 500))).ShouldSucceed();

        (await Rows()).ShouldHaveSingleItem().Reason!.Length.ShouldBe(500);
    }

    [Fact]
    public async Task Execute_Reason501Chars_ReturnsMalformed()
    {
        var p = await Preview("fixture_create_note", new { text = "hello" });

        var e = await Execute("fixture_create_note", new { text = "hello" }, TokenOf(p), new string('r', 501));

        e.ShouldFailWith("request.malformed", HttpStatusCode.BadRequest);
        e.Body.GetProperty("details").GetProperty("path").GetString().ShouldBe("$.reason");
        (await Rows()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Execute_NonStringReason_ReturnsMalformed()
    {
        var p = await Preview("fixture_create_note", new { text = "hello" });

        var e = await Execute("fixture_create_note", new { text = "hello", reason = 42 }, TokenOf(p));

        e.ShouldFailWith("request.malformed", HttpStatusCode.BadRequest);
        e.Body.GetProperty("details").GetProperty("path").GetString().ShouldBe("$.reason");
        (await Rows()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Preview_WithReason_TokenStillValidWithDifferentReason()
    {
        var p = await Preview("fixture_create_note", new { text = "hello", reason = "first thought" });
        p.ShouldSucceed();

        (await Execute("fixture_create_note", new { text = "hello" }, TokenOf(p), "second thought")).ShouldSucceed();

        (await Rows()).ShouldHaveSingleItem().Reason.ShouldBe("second thought");
    }

    [Fact]
    public async Task McpTools_ExecuteSchema_HasOptionalReason()
    {
        var schema = await ToolSchema("fixture_create_note");

        schema["properties"]!["reason"]!["type"]!.GetValue<string>().ShouldBe("string");
        schema["properties"]!["reason"]!["description"]!.GetValue<string>().ShouldBe(
            "Optional. One or two sentences on why you are making this change. Stored in this aggregate's history, where the next agent will read it.");
        schema["required"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldNotContain("reason");
    }

    [Fact]
    public async Task McpTools_PreviewSchema_HasNoReason()
    {
        var schema = await ToolSchema("preview_fixture_create_note");

        schema["properties"]!.AsObject().ContainsKey("reason").ShouldBeFalse();
    }

    [Fact]
    public async Task OpenApi_ExecuteBody_HasOptionalReason()
    {
        var doc = await OpenApiDocument.Fetch(Client);

        var allOf = doc["paths"]!["/api/actions/fixture_create_note"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!["allOf"]!.AsArray();
        var envelope = allOf.Single(s => s!["properties"]?["previewToken"] is not null)!;
        envelope["properties"]!["reason"]!["type"]!.GetValue<string>().ShouldBe("string");
        envelope["required"]!.AsArray().Select(r => r!.GetValue<string>()).ShouldNotContain("reason");
    }

    private async Task<List<HistoryEntry>> Rows()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().History.OrderBy(h => h.Id).ToListAsync(Ct);
    }

    private async Task<JsonNode> ToolSchema(string tool)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(Client.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            Client,
            loggerFactory: null,
            ownsHttpClient: false);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: Ct);
        var tools = await client.ListToolsAsync(cancellationToken: Ct);
        return JsonNode.Parse(tools.Single(t => t.Name == tool).ProtocolTool.InputSchema.GetRawText())!;
    }
}
