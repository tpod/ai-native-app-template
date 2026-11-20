using System.Net;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;
using Case = AppTemplate.Api.Tests.Infrastructure.Case;

namespace AppTemplate.Api.Tests.Features.Projects;

public sealed class ArchiveProjectTests : ProjectTestBase
{
    [Fact]
    [Covers("archive_project", Case.HappyPath)]
    public async Task PreviewAndExecute_ActiveProject_ArchivesAndFreesName()
    {
        var id = await CreateProject("Alpha");

        var (p, e) = await PreviewAndExecute("archive_project", new { projectId = id });

        p.ShouldSucceed();
        p.Body.GetProperty("summary").GetString().ShouldBe("Archive project 'Alpha'.");
        p.Body.GetProperty("sideEffects")[0].GetProperty("event").GetString().ShouldBe("ProjectArchived");
        e.ShouldSucceed();
        ResultOf(e).GetProperty("status").GetString().ShouldBe("archived");
        var reused = await CreateProject("Alpha");
        reused.ShouldNotBe(id);
    }

    [Fact]
    [Covers("archive_project", "project.not_found")]
    public async Task Preview_UnknownProject_ReturnsNotFound()
        => (await Preview("archive_project", new { projectId = Guid.NewGuid() }))
            .ShouldFailWith("project.not_found", HttpStatusCode.NotFound);

    [Fact]
    [Covers("archive_project", "project.archived")]
    public async Task Preview_ArchivedProject_ReturnsArchived()
    {
        var id = await CreateProject("Alpha");
        await ArchiveProject(id);

        (await Preview("archive_project", new { projectId = id }))
            .ShouldFailWith("project.archived", HttpStatusCode.Conflict);
    }

    [Fact]
    [Covers("archive_project", "validation.failed")]
    public async Task Preview_EmptyId_ReturnsNotEmpty()
        => (await Preview("archive_project", new { projectId = Guid.Empty }))
            .ShouldHaveFieldError("projectId", "project_id.not_empty");

    [Fact]
    [Covers("archive_project", Case.Stale)]
    public async Task Execute_TaskAddedSincePreview_ReturnsStaleVersionChanged()
    {
        var id = await CreateProject("Alpha");
        var stale = await Preview("archive_project", new { projectId = id });
        await AddTask(id, "Write docs");

        var e = await Execute("archive_project", new { projectId = id }, TokenOf(stale));

        e.ShouldFailWith("preview.stale", HttpStatusCode.Conflict);
        StaleReasonOf(e).ShouldBe("version_changed");
        (await GetProject(id)).GetProperty("status").GetString().ShouldBe("active");
    }
}
