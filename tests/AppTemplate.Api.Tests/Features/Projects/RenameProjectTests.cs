using System.Net;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;

namespace AppTemplate.Api.Tests.Features.Projects;

public sealed class RenameProjectTests : ProjectTestBase
{
    [Fact]
    [Covers("rename_project", Case.HappyPath)]
    public async Task PreviewAndExecute_NewName_RenamesProject()
    {
        var id = await CreateProject("Alpha");
        var versionBefore = (await GetProject(id)).GetProperty("version").GetInt32();

        var (p, e) = await PreviewAndExecute("rename_project", new { projectId = id, name = "Beta" });

        p.ShouldSucceed();
        p.Body.GetProperty("summary").GetString().ShouldBe("Rename project 'Alpha' to 'Beta'.");
        var change = p.Body.GetProperty("changes").EnumerateArray().ShouldHaveSingleItem();
        change.GetProperty("field").GetString().ShouldBe("name");
        change.GetProperty("before").GetString().ShouldBe("Alpha");
        change.GetProperty("after").GetString().ShouldBe("Beta");
        p.Body.GetProperty("sideEffects")[0].GetProperty("event").GetString().ShouldBe("ProjectRenamed");
        e.ShouldSucceed();
        ResultOf(e).GetProperty("name").GetString().ShouldBe("Beta");
        ResultOf(e).GetProperty("version").GetInt32().ShouldBe(versionBefore + 1);
    }

    [Fact]
    [Covers("rename_project", Case.HappyPath)]
    public async Task PreviewAndExecute_SameName_NoChangesVersionUnchanged()
    {
        var id = await CreateProject("Alpha");
        var versionBefore = (await GetProject(id)).GetProperty("version").GetInt32();
        var other = await Preview("rename_project", new { projectId = id, name = "Beta" });

        var (p, e) = await PreviewAndExecute("rename_project", new { projectId = id, name = "Alpha" });

        p.ShouldSucceed();
        p.Body.GetProperty("changes").GetArrayLength().ShouldBe(0);
        p.Body.GetProperty("warnings")[0].GetProperty("code").GetString().ShouldBe("project.unchanged");
        e.ShouldSucceed();
        e.Body.GetProperty("changes").GetArrayLength().ShouldBe(0);
        (await GetProject(id)).GetProperty("version").GetInt32().ShouldBe(versionBefore);
        (await Execute("rename_project", new { projectId = id, name = "Beta" }, TokenOf(other))).ShouldSucceed();
    }

    [Fact]
    [Covers("rename_project", "project.not_found")]
    public async Task Preview_UnknownProject_ReturnsNotFound()
        => (await Preview("rename_project", new { projectId = Guid.NewGuid(), name = "Beta" }))
            .ShouldFailWith("project.not_found", HttpStatusCode.NotFound);

    [Fact]
    [Covers("rename_project", "project.archived")]
    public async Task Preview_ArchivedProject_ReturnsArchived()
    {
        var id = await CreateProject("Alpha");
        await ArchiveProject(id);

        (await Preview("rename_project", new { projectId = id, name = "Beta" }))
            .ShouldFailWith("project.archived", HttpStatusCode.Conflict);
    }

    [Fact]
    [Covers("rename_project", "project.name_taken")]
    public async Task Preview_NameOfOtherActiveProject_ReturnsNameTaken()
    {
        var alpha = await CreateProject("Alpha");
        var beta = await CreateProject("Beta");

        var p = await Preview("rename_project", new { projectId = beta, name = " ALPHA" });

        p.ShouldFailWith("project.name_taken", HttpStatusCode.Conflict);
        p.Body.GetProperty("details").GetProperty("existingProjectId").GetGuid().ShouldBe(alpha);
    }

    [Fact]
    [Covers("rename_project", "validation.failed")]
    public async Task Preview_EmptyIdAndBlankName_ReturnsBothFieldErrors()
    {
        var p = await Preview("rename_project", new { projectId = Guid.Empty, name = "   " });

        p.ShouldHaveFieldError("projectId", "project_id.not_empty");
        p.ShouldHaveFieldError("name", "project_name.empty");
    }

    [Fact]
    [Covers("rename_project", Case.Stale)]
    public async Task Execute_TaskAddedSincePreview_ReturnsStaleVersionChanged()
    {
        var id = await CreateProject("Alpha");
        var stale = await Preview("rename_project", new { projectId = id, name = "Beta" });
        await AddTask(id, "Write docs");

        var e = await Execute("rename_project", new { projectId = id, name = "Beta" }, TokenOf(stale));

        e.ShouldFailWith("preview.stale", HttpStatusCode.Conflict);
        StaleReasonOf(e).ShouldBe("version_changed");
        (await GetProject(id)).GetProperty("name").GetString().ShouldBe("Alpha");
    }

    [Fact]
    [Covers("rename_project", Case.HappyPath)]
    public async Task PreviewAndExecute_NewName_RecordsRenamedInHistory()
    {
        var id = await CreateProject("Alpha");

        (await PreviewAndExecute("rename_project", new { projectId = id, name = "Beta" })).execute.ShouldSucceed();

        var q = await Query("get_history", new { aggregateType = "Project", aggregateId = id.ToString() });
        q.ShouldSucceed();
        q.Body.GetArrayLength().ShouldBe(2);
        q.Body[0].GetProperty("eventType").GetString().ShouldBe("project.created");
        q.Body[1].GetProperty("eventType").GetString().ShouldBe("project.renamed");
        q.Body[1].GetProperty("payload").GetProperty("newName").GetString().ShouldBe("Beta");
    }
}
