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
    public async Task Model_AfterMigrations_HasNoPendingChanges()
    {
        using var db = new SqliteTestDatabase();
        await using var c = db.NewContext();
        c.Database.HasPendingModelChanges().ShouldBeFalse();
        await Task.CompletedTask;
    }
}
