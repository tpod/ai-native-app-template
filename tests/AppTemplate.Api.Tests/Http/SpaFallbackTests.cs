using System.Net;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;

namespace AppTemplate.Api.Tests.Http;

public sealed class SpaFallbackTests : ApiTestBase, IAsyncDisposable
{
    private readonly string _webRoot = Directory.CreateTempSubdirectory("spa-").FullName;

    protected override ApiFactory CreateFactory()
    {
        File.WriteAllText(Path.Combine(_webRoot, "index.html"), "<html><body>SPA</body></html>");
        return new ApiFactory(webRoot: _webRoot);
    }

    [Fact]
    public async Task Get_ClientRoute_ServesIndexHtml()
    {
        var response = await Client.GetAsync("/projects/123", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("SPA");
    }

    [Fact]
    public async Task Get_UnknownApiRouteWithSpa_StillProblem404()
    {
        var response = await ApiResponse.From(await Client.GetAsync("/api/nope", TestContext.Current.CancellationToken));

        response.ShouldFailWith("request.not_found", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_McpSubRouteWithSpa_StillProblem404()
    {
        var response = await ApiResponse.From(await Client.GetAsync("/mcp/nope", TestContext.Current.CancellationToken));

        response.ShouldFailWith("request.not_found", HttpStatusCode.NotFound);
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await base.DisposeAsync();
        Directory.Delete(_webRoot, true);
    }
}
