using System.Net;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;

namespace AppTemplate.Api.Tests.Features.Projects;

public sealed class GetProjectTests : ProjectTestBase
{
    [Fact]
    [Covers("get_project", Case.HappyPath)]
    public async Task Query_ExistingProject_ReturnsProjectWithTasks()
    {
        var id = await CreateProject("Alpha");
        var done = await AddTask(id, "Write docs");
        var open = await AddTask(id, "Review docs");
        await CompleteTask(id, done);

        var q = await Query("get_project", new { projectId = id });

        q.ShouldSucceed();
        q.Body.GetProperty("id").GetGuid().ShouldBe(id);
        q.Body.GetProperty("name").GetString().ShouldBe("Alpha");
        q.Body.GetProperty("status").GetString().ShouldBe("active");
        var tasks = q.Body.GetProperty("tasks").EnumerateArray().ToDictionary(t => t.GetProperty("id").GetGuid());
        tasks.Count.ShouldBe(2);
        tasks[done].GetProperty("title").GetString().ShouldBe("Write docs");
        tasks[done].GetProperty("done").GetBoolean().ShouldBeTrue();
        tasks[open].GetProperty("title").GetString().ShouldBe("Review docs");
        tasks[open].GetProperty("done").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    [Covers("get_project", "project.not_found")]
    public async Task Query_UnknownProject_ReturnsNotFound()
        => (await Query("get_project", new { projectId = Guid.NewGuid() })).ShouldFailWith("project.not_found", HttpStatusCode.NotFound);

    [Fact]
    [Covers("get_project", "validation.failed")]
    public async Task Query_EmptyId_ReturnsNotEmpty()
        => (await Query("get_project", new { projectId = Guid.Empty })).ShouldHaveFieldError("projectId", "project_id.not_empty");
}
