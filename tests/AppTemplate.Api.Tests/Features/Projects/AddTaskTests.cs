using System.Net;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;
using Case = AppTemplate.Api.Tests.Infrastructure.Case;

namespace AppTemplate.Api.Tests.Features.Projects;

public sealed class AddTaskTests : ProjectTestBase
{
    [Fact]
    [Covers("add_task", Case.HappyPath)]
    public async Task PreviewAndExecute_ActiveProject_AddsOpenTask()
    {
        var id = await CreateProject("Alpha");

        var (p, e) = await PreviewAndExecute("add_task", new { projectId = id, title = "  Write docs " });

        p.ShouldSucceed();
        p.Body.GetProperty("summary").GetString().ShouldBe("Add task 'Write docs' to project 'Alpha'.");
        p.Body.GetProperty("sideEffects")[0].GetProperty("event").GetString().ShouldBe("TaskAdded");
        e.ShouldSucceed();
        var task = ResultOf(e).GetProperty("tasks").EnumerateArray().ShouldHaveSingleItem();
        task.GetProperty("id").GetGuid().ShouldBe(Guid.Parse(IdFromChanges(p, "TaskItem")));
        task.GetProperty("title").GetString().ShouldBe("Write docs");
        task.GetProperty("done").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    [Covers("add_task", "project.not_found")]
    public async Task Preview_UnknownProject_ReturnsNotFound()
        => (await Preview("add_task", new { projectId = Guid.NewGuid(), title = "Write docs" }))
            .ShouldFailWith("project.not_found", HttpStatusCode.NotFound);

    [Fact]
    [Covers("add_task", "project.archived")]
    public async Task Preview_ArchivedProject_ReturnsArchived()
    {
        var id = await CreateProject("Alpha");
        await ArchiveProject(id);

        (await Preview("add_task", new { projectId = id, title = "Write docs" }))
            .ShouldFailWith("project.archived", HttpStatusCode.Conflict);
    }

    [Fact]
    [Covers("add_task", "validation.failed")]
    public async Task Preview_201CharTitle_ReturnsTooLong()
    {
        var id = await CreateProject("Alpha");

        (await Preview("add_task", new { projectId = id, title = new string('t', 201) }))
            .ShouldHaveFieldError("title", "task_title.too_long");
    }

    [Fact]
    [Covers("add_task", Case.Stale)]
    public async Task Execute_ProjectRenamedSincePreview_ReturnsStaleVersionChanged()
    {
        var id = await CreateProject("Alpha");
        var stale = await Preview("add_task", new { projectId = id, title = "Write docs" });
        (await PreviewAndExecute("rename_project", new { projectId = id, name = "Beta" })).execute.ShouldSucceed();

        var e = await Execute("add_task", new { projectId = id, title = "Write docs" }, TokenOf(stale));

        e.ShouldFailWith("preview.stale", HttpStatusCode.Conflict);
        StaleReasonOf(e).ShouldBe("version_changed");
        (await GetProject(id)).GetProperty("tasks").GetArrayLength().ShouldBe(0);
    }
}
