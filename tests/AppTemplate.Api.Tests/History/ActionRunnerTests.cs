using AppTemplate.Api.Actions;
using AppTemplate.Api.Data;
using AppTemplate.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.Tests.History;

/// <summary>In-process callers (imports, jobs, admin tasks) run actions through the same pipeline as HTTP and MCP.</summary>
public sealed class ActionRunnerTests : ApiTestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ActionRunner Runner => Factory.Services.GetRequiredService<ActionRunner>();

    [Fact]
    public async Task RunAsync_CreateNote_WritesInProcessRowWithReason()
    {
        var result = await Runner.RunAsync("fixture_create_note", new { text = "imported" }, "nightly import", Ct);

        result.IsT0.ShouldBeTrue(result.IsT1 ? result.AsT1.Message : "");
        var row = (await Rows()).ShouldHaveSingleItem();
        row.Channel.ShouldBe("in_process");
        row.Reason.ShouldBe("nightly import");
        row.Action.ShouldBe("fixture_create_note");
    }

    [Fact]
    public async Task RunAsync_ValidationFailure_ReturnsValidationFailed()
    {
        var result = await Runner.RunAsync("fixture_create_note", new { text = "" }, null, Ct);

        result.IsT1.ShouldBeTrue();
        result.AsT1.Code.ShouldBe("validation.failed");
        (await Rows()).ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_UnknownAction_ReturnsNotFound()
    {
        var result = await Runner.RunAsync("no_such_action", new { }, null, Ct);

        result.IsT1.ShouldBeTrue();
        result.AsT1.Code.ShouldBe("request.not_found");
    }

    [Fact]
    public async Task RunAsync_QueryName_ReturnsNotFound()
    {
        var result = await Runner.RunAsync("fixture_list_notes", new { }, null, Ct);

        result.IsT1.ShouldBeTrue();
        result.AsT1.Code.ShouldBe("request.not_found");
    }

    private async Task<List<HistoryEntry>> Rows()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().History.OrderBy(h => h.Id).ToListAsync(Ct);
    }
}
