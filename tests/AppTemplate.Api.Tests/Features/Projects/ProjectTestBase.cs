using System.Text.Json;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;

namespace AppTemplate.Api.Tests.Features.Projects;

/// <summary>
/// Setup steps shared by the Projects tests. Each step runs a real action through preview and execute, so a test's
/// arrange phase goes through the same path as its act phase.
/// </summary>
public abstract class ProjectTestBase : ApiTestBase
{
    // No fixtures: these tests exercise only the real app's actions.
    protected override ApiFactory CreateFactory() => new(includeFixtures: false);

    /// <summary>The <c>result</c> of a successful execute: the saved project as a <c>ProjectDto</c>.</summary>
    protected static JsonElement ResultOf(ApiResponse execute) => execute.Body.GetProperty("result");

    /// <summary>The <c>details.reason</c> of a <c>preview.stale</c> error.</summary>
    protected static string? StaleReasonOf(ApiResponse execute) => execute.Body.GetProperty("details").GetProperty("reason").GetString();

    protected async Task<Guid> CreateProject(string name)
    {
        var (_, e) = await PreviewAndExecute("create_project", new { name });
        e.ShouldSucceed();
        return ResultOf(e).GetProperty("id").GetGuid();
    }

    protected async Task<Guid> AddTask(Guid projectId, string title)
    {
        var (p, e) = await PreviewAndExecute("add_task", new { projectId, title });
        e.ShouldSucceed();
        return Guid.Parse(IdFromChanges(p, "TaskItem"));
    }

    protected async Task CompleteTask(Guid projectId, Guid taskId)
        => (await PreviewAndExecute("complete_task", new { projectId, taskId })).execute.ShouldSucceed();

    protected async Task ArchiveProject(Guid projectId)
        => (await PreviewAndExecute("archive_project", new { projectId })).execute.ShouldSucceed();

    /// <summary>The project as <c>get_project</c> returns it now.</summary>
    protected async Task<JsonElement> GetProject(Guid projectId)
    {
        var q = await Query("get_project", new { projectId });
        q.ShouldSucceed();
        return q.Body;
    }
}
