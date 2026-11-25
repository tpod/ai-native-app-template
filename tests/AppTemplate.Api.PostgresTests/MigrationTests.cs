using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace AppTemplate.Api.PostgresTests;

public sealed class MigrationTests(PostgresContainerFixture postgres) : PostgresTestBase(postgres)
{
    [Fact]
    public async Task Migrate_EmptyDatabase_AppliesAllMigrations()
    {
        await using var db = NewContext();

        var all = db.Database.GetMigrations().ToList();
        var applied = (await db.Database.GetAppliedMigrationsAsync(Ct)).ToList();
        var pending = await db.Database.GetPendingMigrationsAsync(Ct);

        all.ShouldNotBeEmpty();
        pending.ShouldBeEmpty();
        applied.ShouldBe(all);
    }
}
