using System.Net;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;

namespace AppTemplate.Api.Tests.Features.Projects;

public sealed class CompleteTaskTests : ProjectTestBase
{
    [Fact]
    [Covers("complete_task", Case.HappyPath)]
    public async Task PreviewAndExecute_OpenTask_MarksTaskDone()
    {
        var id = await CreateProject("Alpha");
        var taskId = await AddTask(id, "Write docs");

        var (p, e) = await PreviewAndExecute("complete_task", new { projectId = id, taskId });

        p.ShouldSucceed();
        p.Body.GetProperty("summary").GetString().ShouldBe("Complete task 'Write docs' in project 'Alpha'.");
        p.Body.GetProperty("sideEffects")[0].GetProperty("event").GetString().ShouldBe("TaskCompleted");
        e.ShouldSucceed();
        var task = ResultOf(e).GetProperty("tasks").EnumerateArray().ShouldHaveSingleItem();
        task.GetProperty("id").GetGuid().ShouldBe(taskId);
        task.GetProperty("done").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    [Covers("complete_task", "project.not_found")]
    public async Task Preview_UnknownProject_ReturnsNotFound()
        => (await Preview("complete_task", new { projectId = Guid.NewGuid(), taskId = Guid.NewGuid() }))
            .ShouldFailWith("project.not_found", HttpStatusCode.NotFound);

    [Fact]
    [Covers("complete_task", "project.archived")]
    public async Task Preview_ArchivedProject_ReturnsArchived()
    {
        var id = await CreateProject("Alpha");
        var taskId = await AddTask(id, "Write docs");
        await ArchiveProject(id);

        (await Preview("complete_task", new { projectId = id, taskId }))
            .ShouldFailWith("project.archived", HttpStatusCode.Conflict);
    }

    [Fact]
    [Covers("complete_task", "task.not_found")]
    public async Task Preview_UnknownTask_ReturnsTaskNotFound()
    {
        var id = await CreateProject("Alpha");

        (await Preview("complete_task", new { projectId = id, taskId = Guid.NewGuid() }))
            .ShouldFailWith("task.not_found", HttpStatusCode.NotFound);
    }

    [Fact]
    [Covers("complete_task", "task.already_done")]
    public async Task Preview_DoneTask_ReturnsAlreadyDone()
    {
        var id = await CreateProject("Alpha");
        var taskId = await AddTask(id, "Write docs");
        await CompleteTask(id, taskId);

        (await Preview("complete_task", new { projectId = id, taskId }))
            .ShouldFailWith("task.already_done", HttpStatusCode.Conflict);
    }

    [Fact]
    [Covers("complete_task", "validation.failed")]
    public async Task Preview_EmptyIds_ReturnsBothFieldErrors()
    {
        var p = await Preview("complete_task", new { projectId = Guid.Empty, taskId = Guid.Empty });

        p.ShouldHaveFieldError("projectId", "project_id.not_empty");
        p.ShouldHaveFieldError("taskId", "task_id.not_empty");
    }

    [Fact]
    [Covers("complete_task", Case.Stale)]
    public async Task Execute_TaskAddedSincePreview_ReturnsStaleVersionChanged()
    {
        var id = await CreateProject("Alpha");
        var taskId = await AddTask(id, "Write docs");
        var stale = await Preview("complete_task", new { projectId = id, taskId });
        await AddTask(id, "Review docs");

        var e = await Execute("complete_task", new { projectId = id, taskId }, TokenOf(stale));

        e.ShouldFailWith("preview.stale", HttpStatusCode.Conflict);
        StaleReasonOf(e).ShouldBe("version_changed");
        (await GetProject(id)).GetProperty("tasks").EnumerateArray()
            .Single(t => t.GetProperty("id").GetGuid() == taskId).GetProperty("done").GetBoolean().ShouldBeFalse();
    }
}
