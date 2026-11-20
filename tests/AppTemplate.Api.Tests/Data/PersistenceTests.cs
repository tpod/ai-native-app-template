using AppTemplate.Api.Actions.Preview;
using AppTemplate.Api.Data;
using AppTemplate.Api.Tests.Infrastructure;
using AppTemplate.Domain.Common;
using AppTemplate.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace AppTemplate.Api.Tests.Data;

public sealed class PersistenceTests
{
    private static Project NewProject(string name, ProjectId id)
        => Project.Create(id, ProjectName.Create(name).AsT0, null).AsT0;

    [Fact]
    public async Task SaveChanges_ProjectWithTasks_RoundTripsThroughFreshContext()
    {
        using var db = new SqliteTestDatabase();
        db.Migrate();
        var id = ProjectId.From(Guid.NewGuid());
        var p = NewProject("Alpha", id);
        p.AddTask(TaskItemId.From(Guid.NewGuid()), TaskTitle.Create("T1").AsT0);
        await using (var c = db.NewContext())
        {
            c.Projects.Add(p);
            await c.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var c2 = db.NewContext();
        var loaded = await c2.Projects.Include(x => x.Tasks).SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);
        loaded.Name.Value.ShouldBe("Alpha");
        loaded.Tasks.ShouldHaveSingleItem().Title.Value.ShouldBe("T1");
    }

    [Fact]
    public async Task SaveChanges_ConcurrentVersionBump_SecondThrowsConcurrency()
    {
        using var db = new SqliteTestDatabase();
        db.Migrate();
        var id = ProjectId.From(Guid.NewGuid());
        await using (var seed = db.NewContext())
        {
            seed.Projects.Add(NewProject("Alpha", id));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var c1 = db.NewContext();
        await using var c2 = db.NewContext();
        var p1 = await c1.Projects.SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);
        var p2 = await c2.Projects.SingleAsync(x => x.Id == id, TestContext.Current.CancellationToken);
        ((IAggregateRoot)p1).IncrementVersion();
        ((IAggregateRoot)p2).IncrementVersion();

        await c1.SaveChangesAsync(TestContext.Current.CancellationToken);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => c2.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Query_NameDiffersOnlyInCase_Matches()
    {
        using var db = new SqliteTestDatabase();
        db.Migrate();
        await using (var c = db.NewContext())
        {
            c.Projects.Add(NewProject("Alpha", ProjectId.From(Guid.NewGuid())));
            await c.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var c2 = db.NewContext();
        var lower = ProjectName.Create("alpha").AsT0;
        (await c2.Projects.AnyAsync(p => p.Name == lower, TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public void ChangeSet_AddedProjectWithTask_ListsAllFieldsIncludingChildProjectId()
    {
        using var db = new SqliteTestDatabase();
        db.Migrate();
        using var ctx = db.NewContext();
        var pid = ProjectId.From(Guid.NewGuid());
        var tid = TaskItemId.From(Guid.NewGuid());
        var project = NewProject("Alpha", pid);
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
    public async Task Model_AfterMigrations_HasNoPendingChanges()
    {
        using var db = new SqliteTestDatabase();
        await using var c = db.NewContext();
        c.Database.HasPendingModelChanges().ShouldBeFalse();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task History_RoundTripsEveryColumn()
    {
        using var db = new SqliteTestDatabase();
        db.Migrate();
        var occurredAt = new DateTimeOffset(2026, 11, 20, 10, 30, 0, TimeSpan.Zero);
        var executionId = Guid.NewGuid();
        var written = new HistoryEntry(
            aggregateType: "Project",
            aggregateId: "0f8fad5b-d9cb-469f-a165-70867728950e",
            aggregateVersion: 3,
            eventType: "project.renamed",
            eventVersion: 1,
            payload: """{"oldName":"A","newName":"B"}""",
            occurredAt: occurredAt,
            executionId: executionId,
            action: "rename_project",
            channel: "mcp",
            client: "claude-code",
            reason: "customer asked",
            traceId: "4bf92f3577b34da6a3ce929d0e0e4736");
        await using (var c = db.NewContext())
        {
            c.History.Add(written);
            await c.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var c2 = db.NewContext();
        var read = await c2.History.SingleAsync(TestContext.Current.CancellationToken);

        read.Id.ShouldBeGreaterThan(0);
        read.AggregateType.ShouldBe("Project");
        read.AggregateId.ShouldBe("0f8fad5b-d9cb-469f-a165-70867728950e");
        read.AggregateVersion.ShouldBe(3);
        read.EventType.ShouldBe("project.renamed");
        read.EventVersion.ShouldBe(1);
        read.Payload.ShouldBe("""{"oldName":"A","newName":"B"}""");
        read.OccurredAt.ShouldBe(occurredAt);
        read.ExecutionId.ShouldBe(executionId);
        read.Action.ShouldBe("rename_project");
        read.Channel.ShouldBe("mcp");
        read.Client.ShouldBe("claude-code");
        read.Reason.ShouldBe("customer asked");
        read.TraceId.ShouldBe("4bf92f3577b34da6a3ce929d0e0e4736");
    }
}
