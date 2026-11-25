using AppTemplate.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Shouldly;

namespace AppTemplate.Architecture.Tests;

/// <summary>
/// The app runs on Postgres and the fast tests on SQLite, each with its own migration set. These tests keep the two
/// in step without opening a connection: both are current, both have the same shape, and provider-specific mapping
/// is applied on both.
/// </summary>
public sealed class ProviderModelTests
{
    [Theory]
    [InlineData(typeof(PostgresAppDbContext))]
    [InlineData(typeof(SqliteAppDbContext))]
    public void Migrations_AreUpToDate(Type contextType)
    {
        using var context = NewContext(contextType);

        context.Database.HasPendingModelChanges()
            .ShouldBeFalse($"Run scripts/add-migration.sh: {contextType.Name} has model changes without a migration.");
    }

    [Fact]
    public void ProviderModels_HaveSameTablesAndColumns()
    {
        using var postgres = NewContext(typeof(PostgresAppDbContext));
        using var sqlite = NewContext(typeof(SqliteAppDbContext));

        var postgresShape = Shape(postgres);
        var sqliteShape = Shape(sqlite);

        postgresShape.Count.ShouldBeGreaterThan(0);
        postgresShape.ShouldBe(sqliteShape, ignoreOrder: true);
    }

    [Fact(Skip = GuardConditions.NoAggregateReason, SkipType = typeof(GuardConditions), SkipUnless = nameof(GuardConditions.DomainHasAggregate))]
    public void CaseInsensitiveText_IsMappedOnBothProviders()
    {
        using var postgres = NewContext(typeof(PostgresAppDbContext));
        using var sqlite = NewContext(typeof(SqliteAppDbContext));

        var postgresProperties = CaseInsensitiveProperties(postgres);
        var sqliteProperties = CaseInsensitiveProperties(sqlite);

        postgresProperties.ShouldNotBeEmpty();
        sqliteProperties.Count.ShouldBe(postgresProperties.Count);
        foreach (var property in postgresProperties)
        {
            property.GetColumnType().ShouldBe("citext", $"{property.DeclaringType.DisplayName()}.{property.Name}");
        }

        foreach (var property in sqliteProperties)
        {
            property.GetCollation().ShouldBe("NOCASE", $"{property.DeclaringType.DisplayName()}.{property.Name}");
        }
    }

    private static AppDbContext NewContext(Type contextType)
    {
        // Builds the model only; neither provider opens a connection for that.
        if (contextType == typeof(PostgresAppDbContext))
        {
            var options = new DbContextOptionsBuilder<PostgresAppDbContext>().UseNpgsql("Host=localhost;Database=model_only").Options;
            return new PostgresAppDbContext(options, []);
        }

        var sqliteOptions = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite("DataSource=:memory:").Options;
        return new SqliteAppDbContext(sqliteOptions, []);
    }

    private static List<string> Shape(DbContext context)
        => [.. context.Model.GetEntityTypes()
            .Where(e => e.GetTableName() is not null)
            .SelectMany(e =>
            {
                var table = StoreObjectIdentifier.Table(e.GetTableName()!, e.GetSchema());
                return e.GetProperties().Select(p => $"{e.GetTableName()}.{p.GetColumnName(table)}");
            })
            .Distinct()];

    // Column types and collations live only in the design-time model, not the read-optimized runtime one.
    private static List<IProperty> CaseInsensitiveProperties(DbContext context)
        => [.. context.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => p.FindAnnotation(CaseInsensitiveText.Annotation)?.Value is true)];
}
