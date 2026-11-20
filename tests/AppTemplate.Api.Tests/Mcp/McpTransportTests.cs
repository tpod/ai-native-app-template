using System.Net;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;

namespace AppTemplate.Api.Tests.Mcp;

/// <summary>
/// Streamable HTTP on a stateless server: only POST carries messages, so GET (SSE stream) and DELETE (end session)
/// on the endpoint must answer 405 with <c>Allow: POST</c>, never 404, or clients treat the server as dead.
/// </summary>
public sealed class McpTransportTests : ApiTestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_McpEndpoint_Returns405AllowPost()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await Client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.ShouldBe(["POST"]);
    }

    [Fact]
    public async Task Delete_McpEndpoint_Returns405AllowPost()
    {
        using var response = await Client.DeleteAsync("/mcp", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        response.Content.Headers.Allow.ShouldBe(["POST"]);
    }

    [Fact]
    public async Task Get_McpSubRoute_StillReturns404Problem()
    {
        var response = await ApiResponse.From(await Client.GetAsync("/mcp/nope", Ct));

        response.ShouldFailWith("request.not_found", HttpStatusCode.NotFound);
    }
}
