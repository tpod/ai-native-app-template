using System.Net;
using AppTemplate.Api.Tests.Infrastructure;
using Shouldly;

namespace AppTemplate.Api.Tests.Features.Projects;

public sealed class CreateProjectTests : ProjectTestBase
{
    [Fact]
    [Covers("create_project", Case.HappyPath)]
    public async Task PreviewAndExecute_NewName_CreatesActiveProject()
    {
        var (p, e) = await PreviewAndExecute("create_project", new { name = "Alpha" });

        p.ShouldSucceed();
        p.Body.GetProperty("summary").GetString().ShouldBe("Create project 'Alpha'.");
        p.Body.GetProperty("sideEffects")[0].GetProperty("event").GetString().ShouldBe("ProjectCreated");
        e.ShouldSucceed();
        var result = ResultOf(e);
        var id = result.GetProperty("id").GetGuid();
        id.ShouldBe(Guid.Parse(IdFromChanges(p, "Project")));
        result.GetProperty("name").GetString().ShouldBe("Alpha");
        result.GetProperty("status").GetString().ShouldBe("active");
        result.GetProperty("tasks").GetArrayLength().ShouldBe(0);
        (await GetProject(id)).GetProperty("name").GetString().ShouldBe("Alpha");
    }

    [Fact]
    [Covers("create_project", "project.name_taken")]
    public async Task Preview_NameDiffersOnlyByCaseAndSpaces_ReturnsNameTaken()
    {
        var existing = await CreateProject("Alpha");

        var p = await Preview("create_project", new { name = "  alpha " });

        p.ShouldFailWith("project.name_taken", HttpStatusCode.Conflict);
        p.Body.GetProperty("details").GetProperty("existingProjectId").GetGuid().ShouldBe(existing);
    }

    [Fact]
    [Covers("create_project", "validation.failed")]
    public async Task Preview_101CharName_ReturnsTooLong()
        => (await Preview("create_project", new { name = new string('a', 101) })).ShouldHaveFieldError("name", "project_name.too_long");

    [Fact]
    [Covers("create_project", Case.Stale)]
    public async Task Execute_SameNameCreatedSincePreview_ReturnsStale()
    {
        var stale = await Preview("create_project", new { name = "Alpha" });
        await CreateProject("Alpha");

        var e = await Execute("create_project", new { name = "Alpha" }, TokenOf(stale));

        e.ShouldFailWith("preview.stale", HttpStatusCode.Conflict);
        (await Query("list_projects", new { })).Body.GetArrayLength().ShouldBe(1);
    }
}
