using AppTemplate.Api.Data;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace AppTemplate.Api.PostgresTests;

/// <summary>
/// The Postgres migration set applies to an empty database, and every test database cloned from the run's
/// template starts fully migrated, so no test pays for migrations.
/// </summary>
public sealed class MigrationTests(PostgresContainerFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Migrate_EmptyDatabase_AppliesAllMigrations()
    {
        await using var db = NewContext(await postgres.CreateEmptyDatabaseAsync());

        await db.Database.MigrateAsync(Ct);

        var all = db.Database.GetMigrations().ToList();
        all.ShouldNotBeEmpty();
        (await db.Database.GetPendingMigrationsAsync(Ct)).ShouldBeEmpty();
        (await db.Database.GetAppliedMigrationsAsync(Ct)).ShouldBe(all);
    }

    [Fact]
    public async Task CreateDatabase_FromTemplate_StartsFullyMigrated()
    {
        await using var db = NewContext(await postgres.CreateDatabaseAsync());

        var applied = (await db.Database.GetAppliedMigrationsAsync(Ct)).ToList();

        var all = db.Database.GetMigrations().ToList();
        all.ShouldNotBeEmpty();
        applied.ShouldBe(all);
    }

    private static PostgresAppDbContext NewContext(string connectionString)
        => new(new DbContextOptionsBuilder<PostgresAppDbContext>().UseNpgsql(connectionString).Options, []);
}
