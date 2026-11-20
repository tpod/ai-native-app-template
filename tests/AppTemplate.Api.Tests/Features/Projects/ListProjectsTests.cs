using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;
using Case = AppTemplate.Api.Tests.Infrastructure.Case;

namespace AppTemplate.Api.Tests.Features.Projects;

public sealed class ListProjectsTests : ProjectTestBase
{
    [Fact]
    [Covers("list_projects", Case.HappyPath)]
    public async Task Query_NoFilter_ReturnsAllOrderedByNameWithTaskCounts()
    {
        // "Beta" sorts before "alpha" by ordinal; the expected order proves the case-insensitive sort.
        var beta = await CreateProject("Beta");
        await CreateProject("alpha");
        var done = await AddTask(beta, "Write docs");
        await AddTask(beta, "Review docs");
        await CompleteTask(beta, done);

        var q = await Query("list_projects", new { });

        q.ShouldSucceed();
        q.Body.EnumerateArray().Select(p => p.GetProperty("name").GetString()).ShouldBe(["alpha", "Beta"]);
        var summary = q.Body.EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == beta);
        summary.GetProperty("status").GetString().ShouldBe("active");
        summary.GetProperty("taskCount").GetInt32().ShouldBe(2);
        summary.GetProperty("openTaskCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    [Covers("list_projects", Case.HappyPath)]
    public async Task Query_StatusArchived_ReturnsOnlyArchivedProjects()
    {
        await CreateProject("Alpha");
        var archived = await CreateProject("Beta");
        await ArchiveProject(archived);

        var q = await Query("list_projects", new { status = "archived" });

        q.ShouldSucceed();
        var only = q.Body.EnumerateArray().ShouldHaveSingleItem();
        only.GetProperty("id").GetGuid().ShouldBe(archived);
        only.GetProperty("status").GetString().ShouldBe("archived");
    }

    [Fact]
    [Covers("list_projects", "validation.failed")]
    public async Task Query_UndefinedStatusNumber_ReturnsValidationFailed()
        => (await Query("list_projects", new { status = 7 })).ShouldHaveFieldError("status", "status.enum");
}
