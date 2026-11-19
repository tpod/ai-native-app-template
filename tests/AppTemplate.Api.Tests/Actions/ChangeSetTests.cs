using System.Text.Json.Nodes;
using AppTemplate.Api.Actions;
using AppTemplate.Api.Actions.Preview;
using AppTemplate.Api.Tests.Fixtures.Notes;
using AppTemplate.Api.Tests.Infrastructure;
using AppTemplate.Domain.Common;
using AppTemplate.Domain.Projects;
using Shouldly;

namespace AppTemplate.Api.Tests.Actions;

public sealed class ChangeSetTests
{
    private static readonly NoteModelContributor Notes = new();

    [Fact]
    public async Task Read_ModifiedNote_ReturnsTextBeforeAfterOnly()
    {
        using var db = new SqliteTestDatabase();
        db.Migrate(Notes);
        var id = NoteId.From(Guid.NewGuid());
        await using (var seed = db.NewContext(Notes))
        {
            seed.Set<Note>().Add(Note.Create(id, NoteText.Create("a").AsT0));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var ctx = db.NewContext(Notes);
        var note = await ctx.Set<Note>().FindAsync([id], TestContext.Current.CancellationToken);
        note!.Edit(NoteText.Create("b").AsT0);
        ((IAggregateRoot)note).IncrementVersion();

        var c = ChangeSetReader.Read(ctx.ChangeTracker).ShouldHaveSingleItem();

        (c.Entity, c.Id, c.Op, c.Field).ShouldBe(("Note", id.Value.ToString("D"), "modified", "text"));
        c.Before!.GetValue<string>().ShouldBe("a");
        c.After!.GetValue<string>().ShouldBe("b");
    }

    [Fact]
    public void Read_AddedProjectWithTask_ListsAllFieldsIncludingChildProjectId()
    {
        using var db = new SqliteTestDatabase();
        db.Migrate();
        using var ctx = db.NewContext();
        var pid = ProjectId.From(Guid.NewGuid());
        var tid = TaskItemId.From(Guid.NewGuid());
        var project = Project.Create(pid, ProjectName.Create("Alpha").AsT0, null).AsT0;
        project.AddTask(tid, TaskTitle.Create("T1").AsT0);
        ctx.Projects.Add(project);

        var changes = ChangeSetReader.Read(ctx.ChangeTracker);

        changes.ShouldAllBe(c => c.Op == "added" && c.Before == null);
        changes.Select(c => $"{c.Entity}.{c.Field}").ShouldBe(
            [
                "Project.id", "Project.name", "Project.status",
                "TaskItem.done", "TaskItem.id", "TaskItem.projectId", "TaskItem.title",
            ],
            ignoreOrder: false);
        changes.Single(c => c.Field == "projectId").After!.GetValue<string>().ShouldBe(pid.Value.ToString("D"));
        changes.Single(c => c.Entity == "TaskItem" && c.Field == "id").Id.ShouldBe(tid.Value.ToString("D"));
        changes.Single(c => c.Field == "done").After!.GetValue<bool>().ShouldBeFalse();
    }

    [Fact]
    public void Read_VersionIncremented_IsExcluded()
    {
        using var db = new SqliteTestDatabase();
        db.Migrate(Notes);
        using var ctx = db.NewContext(Notes);
        var note = Note.Create(NoteId.From(Guid.NewGuid()), NoteText.Create("a").AsT0);
        ctx.Set<Note>().Add(note);

        ChangeSetReader.Read(ctx.ChangeTracker).ShouldNotContain(c => c.Field == "version");
        ChangeSetReader.Read(ctx.ChangeTracker).Select(c => c.Field).ShouldBe(["id", "locked", "text"]);
    }

    private static Change Row(string id, string field)
        => new("Note", id, "modified", field, JsonValue.Create("a"), JsonValue.Create("b"));

    [Fact]
    public void Hash_SameChangesDifferentOrder_IsEqual()
    {
        var one = ChangeSetHasher.Hash([Row("1", "text"), Row("2", "text")], [], []);
        var two = ChangeSetHasher.Hash([Row("2", "text"), Row("1", "text")], [], []);

        two.ShouldBe(one);
        one.Length.ShouldBe(64);
    }

    [Fact]
    public void Hash_DifferentWarning_Differs()
    {
        var changes = new[] { Row("1", "text") };
        var a = ChangeSetHasher.Hash(changes, [], [new PreviewWarning("w.one", "m")]);
        var b = ChangeSetHasher.Hash(changes, [], [new PreviewWarning("w.two", "m")]);

        b.ShouldNotBe(a);
    }

    [Fact]
    public void Hash_ObjectKeyOrderInPayload_IsIgnored()
    {
        var changes = new[] { Row("1", "text") };
        var a = ChangeSetHasher.Hash(changes, [new SideEffect("e", new JsonObject { ["x"] = 1, ["y"] = 2 })], []);
        var b = ChangeSetHasher.Hash(changes, [new SideEffect("e", new JsonObject { ["y"] = 2, ["x"] = 1 })], []);

        b.ShouldBe(a);
    }
}
