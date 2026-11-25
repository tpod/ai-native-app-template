using System.Net;
using Shouldly;

namespace AppTemplate.Api.PostgresTests;

/// <summary>Project names are case-insensitive on Postgres (citext), as NOCASE makes them on SQLite.</summary>
public sealed class ProjectNamesTests(PostgresContainerFixture postgres) : PostgresTestBase(postgres)
{
    [Fact]
    public async Task CreateProject_NameDiffersOnlyByCase_ReturnsNameTaken()
    {
        await PreviewAndExecute("create_project", new { name = "Alpha" });

        var (status, body) = await Preview("create_project", new { name = " alpha " });

        status.ShouldBe(HttpStatusCode.Conflict);
        body.GetProperty("code").GetString().ShouldBe("project.name_taken");
    }

    [Fact]
    public async Task ListProjects_MixedCase_OrdersCaseInsensitively()
    {
        foreach (var name in new[] { "charlie", "Beta", "alpha" })
        {
            await PreviewAndExecute("create_project", new { name });
        }

        var (status, body) = await Query("list_projects", new { });

        status.ShouldBe(HttpStatusCode.OK);
        var names = body.EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToList();
        names.ShouldBe(["alpha", "Beta", "charlie"]);
    }
}
