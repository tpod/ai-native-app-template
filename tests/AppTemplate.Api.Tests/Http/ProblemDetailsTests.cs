using System.Net;
using AppTemplate.Api.Actions.Http;
using AppTemplate.Api.Tests.Infrastructure;
using AppTemplate.Domain.Common;
using Shouldly;

namespace AppTemplate.Api.Tests.Http;

public sealed class ProblemDetailsTests : ApiTestBase
{
    [Fact]
    public async Task Get_UnknownApiRoute_Returns404ProblemWithCode()
    {
        var r = await ApiResponse.From(await Client.GetAsync("/api/nope", TestContext.Current.CancellationToken));
        r.ShouldFailWith("request.not_found", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_UnknownMcpSubRoute_Returns404Problem()
    {
        var r = await ApiResponse.From(await Client.GetAsync("/mcp/nope", TestContext.Current.CancellationToken));
        r.ShouldFailWith("request.not_found", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Post_UnknownMcpSubRoute_Returns404ProblemNotMcp()
    {
        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var r = await ApiResponse.From(await Client.PostAsync("/mcp/nope", content, TestContext.Current.CancellationToken));
        r.ShouldFailWith("request.not_found", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_Health_HasTraceIdHeader()
    {
        var r = await ApiResponse.From(await Client.GetAsync("/health", TestContext.Current.CancellationToken));
        r.Status.ShouldBe(HttpStatusCode.OK);
        r.Headers.TryGetValues("X-Trace-Id", out var values).ShouldBeTrue();
        values!.Single().ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void StatusFor_EachKind_MatchesTable()
    {
        AppErrorProblemDetails.StatusFor(ErrorKind.Validation).ShouldBe(422);
        AppErrorProblemDetails.StatusFor(ErrorKind.NotFound).ShouldBe(404);
        AppErrorProblemDetails.StatusFor(ErrorKind.Conflict).ShouldBe(409);
        AppErrorProblemDetails.StatusFor(ErrorKind.Precondition).ShouldBe(409);
        AppErrorProblemDetails.StatusFor(ErrorKind.BadRequest).ShouldBe(400);
        AppErrorProblemDetails.StatusFor(ErrorKind.Forbidden).ShouldBe(403);
        AppErrorProblemDetails.StatusFor(ErrorKind.Unexpected).ShouldBe(500);
    }

    [Fact]
    public void StatusFor_EveryKind_IsMapped()
    {
        var kinds = Enum.GetValues<ErrorKind>();
        kinds.Length.ShouldBe(7);

        kinds.Select(AppErrorProblemDetails.StatusFor).ShouldAllBe(s => s >= 400 && s <= 599);
    }
}
