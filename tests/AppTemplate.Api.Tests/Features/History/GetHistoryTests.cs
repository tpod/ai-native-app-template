using System.Net;
using AppTemplate.Api.Tests.Features.Projects;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;

namespace AppTemplate.Api.Tests.Features.History;

public sealed class GetHistoryTests : ProjectTestBase
{
    [Fact]
    [Covers("get_history", Case.HappyPath)]
    public async Task Query_RenamedProject_ReturnsCreatedThenRenamed()
    {
        var id = await CreateProject("Alpha");
        (await PreviewAndExecute("rename_project", new { projectId = id, name = "Beta" })).execute.ShouldSucceed();

        var q = await Query("get_history", new { aggregateType = "Project", aggregateId = id.ToString() });

        q.ShouldSucceed();
        q.Body.GetArrayLength().ShouldBe(2);
        q.Body[0].GetProperty("eventType").GetString().ShouldBe("project.created");
        q.Body[1].GetProperty("eventType").GetString().ShouldBe("project.renamed");
        q.Body[1].GetProperty("payload").GetProperty("newName").GetString().ShouldBe("Beta");
        q.Body[1].GetProperty("action").GetString().ShouldBe("rename_project");
        q.Body[1].GetProperty("channel").GetString().ShouldBe("http");
        q.Body[1].GetProperty("aggregateVersion").GetInt32().ShouldBe(2);
    }

    [Fact]
    [Covers("get_history", "history.unknown_aggregate_type")]
    public async Task Query_UnknownType_ReturnsUnknownAggregateType()
    {
        var q = await Query("get_history", new { aggregateType = "Invoice", aggregateId = Guid.NewGuid().ToString() });

        q.ShouldFailWith("history.unknown_aggregate_type", HttpStatusCode.BadRequest);
        q.Body.GetProperty("details").GetProperty("known").EnumerateArray().Select(k => k.GetString()).ShouldContain("Project");
    }

    [Fact]
    [Covers("get_history", "validation.failed")]
    public async Task Query_LimitZero_ReturnsValidationFailed()
        => (await Query("get_history", new { aggregateType = "Project", aggregateId = Guid.NewGuid().ToString(), limit = 0 }))
            .ShouldHaveFieldError("limit", "history.limit_out_of_range");

    [Fact]
    public async Task Query_EmptyAggregateId_ReturnsValidationFailed()
        => (await Query("get_history", new { aggregateType = "Project", aggregateId = "" }))
            .ShouldHaveFieldError("aggregateId", "history.field_required");

    [Fact]
    public async Task Query_LowercaseType_MatchesProject()
    {
        var id = await CreateProject("Alpha");

        var q = await Query("get_history", new { aggregateType = "project", aggregateId = id.ToString() });

        q.ShouldSucceed();
        q.Body.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Query_UppercaseGuid_MatchesRows()
    {
        var id = await CreateProject("Alpha");

        var q = await Query("get_history", new { aggregateType = "Project", aggregateId = id.ToString("B").ToUpperInvariant() });

        q.ShouldSucceed();
        q.Body.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Query_AfterIdPastEnd_ReturnsEmpty()
    {
        var id = await CreateProject("Alpha");
        var all = await Query("get_history", new { aggregateType = "Project", aggregateId = id.ToString() });
        var last = all.Body[0].GetProperty("id").GetInt64();

        var q = await Query("get_history", new { aggregateType = "Project", aggregateId = id.ToString(), afterId = last });

        q.ShouldSucceed();
        q.Body.GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task Query_Limit1_ReturnsOnlyFirst()
    {
        var id = await CreateProject("Alpha");
        await AddTask(id, "T1");

        var q = await Query("get_history", new { aggregateType = "Project", aggregateId = id.ToString(), limit = 1 });

        q.ShouldSucceed();
        q.Body.GetArrayLength().ShouldBe(1);
        q.Body[0].GetProperty("eventType").GetString().ShouldBe("project.created");
    }

    [Fact]
    public async Task Query_NoRows_ReturnsEmpty()
    {
        var q = await Query("get_history", new { aggregateType = "Project", aggregateId = Guid.NewGuid().ToString() });

        q.ShouldSucceed();
        q.Body.GetArrayLength().ShouldBe(0);
    }
}
