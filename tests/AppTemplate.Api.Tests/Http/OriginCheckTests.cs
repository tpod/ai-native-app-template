using System.Net;
using System.Text;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;

namespace AppTemplate.Api.Tests.Http;

/// <summary>
/// DNS-rebinding protection: a browser request to <c>/api</c> or <c>/mcp</c> whose <c>Origin</c> is not allowed gets
/// <c>403 request.forbidden_origin</c>. Loopback origins and requests without an Origin (agents, curl) pass.
/// </summary>
public sealed class OriginCheckTests : ApiTestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/queries/fixture_list_notes")]
    [InlineData("/mcp")]
    public async Task Post_EvilOrigin_Returns403ForbiddenOrigin(string path)
    {
        var r = await PostWithOrigin(Client, path, "http://evil.example");

        r.ShouldFailWith("request.forbidden_origin", HttpStatusCode.Forbidden);
        r.Body.GetProperty("details").GetProperty("origin").GetString().ShouldBe("http://evil.example");
        r.Body.GetProperty("suggestion").GetString()!.ShouldContain("Security:AllowedOrigins");
        r.Headers.Contains("X-Trace-Id").ShouldBeTrue();
    }

    [Theory]
    [InlineData("http://localhost:5173")]
    [InlineData("https://localhost:7001")]
    [InlineData("http://127.0.0.1:5180")]
    [InlineData("http://localhost")]
    public async Task Post_LoopbackOrigin_Passes(string origin)
    {
        var r = await PostWithOrigin(Client, "/api/queries/fixture_list_notes", origin);

        r.ShouldSucceed();
    }

    [Fact]
    public async Task Post_NoOrigin_Passes()
    {
        var r = await PostWithOrigin(Client, "/api/queries/fixture_list_notes", origin: null);

        r.ShouldSucceed();
    }

    [Fact]
    public async Task Post_LookalikeLoopbackHost_Returns403()
    {
        var r = await PostWithOrigin(Client, "/api/queries/fixture_list_notes", "http://localhost.evil.example:5173");

        r.ShouldFailWith("request.forbidden_origin", HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_EvilOriginOutsideApiAndMcp_IsNotChecked()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("Origin", "http://evil.example");

        using var response = await Client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    internal static async Task<ApiResponse> PostWithOrigin(HttpClient client, string path, string? origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return await ApiResponse.From(await client.SendAsync(request, Ct));
    }
}

/// <summary>An origin listed in <c>Security:AllowedOrigins</c> passes; others still get 403.</summary>
public sealed class OriginCheckConfiguredTests : ApiTestBase
{
    protected override ApiFactory CreateFactory()
    {
        var factory = new ApiFactory();
        factory.Settings["Security:AllowedOrigins:0"] = "https://app.example.com";
        return factory;
    }

    [Fact]
    public async Task Post_ConfiguredOrigin_Passes()
    {
        var r = await OriginCheckTests.PostWithOrigin(Client, "/api/queries/fixture_list_notes", "https://app.example.com");

        r.ShouldSucceed();
    }

    [Fact]
    public async Task Post_UnlistedOriginWithConfiguredList_Returns403()
    {
        var r = await OriginCheckTests.PostWithOrigin(Client, "/api/queries/fixture_list_notes", "https://other.example.com");

        r.ShouldFailWith("request.forbidden_origin", HttpStatusCode.Forbidden);
    }
}
