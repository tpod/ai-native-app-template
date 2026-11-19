using AppTemplate.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Api.Tests.Infrastructure;

/// <summary>One shared in-memory SQLite database; contexts come and go, the connection keeps the data alive.</summary>
internal sealed class SqliteTestDatabase : IDisposable
{
    public SqliteTestDatabase() => Connection.Open();

    public SqliteConnection Connection { get; } = new("DataSource=:memory:");

    public AppDbContext NewContext(params IModelContributor[] contributors)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Connection).Options;
        return new AppDbContext(options, contributors);
    }

    public void Migrate(params IModelContributor[] contributors)
    {
        using var context = NewContext(contributors);
        Migrate(context, contributors);
    }

    /// <summary>Applies EF migrations through <paramref name="context"/>, then runs each contributor's raw DDL.</summary>
    public void Migrate(AppDbContext context, IEnumerable<IModelContributor> contributors)
    {
        context.Database.Migrate();
        foreach (var contributor in contributors)
        {
            // Plain ADO: ExecuteSqlRaw would treat '{' and '}' in the DDL as format placeholders.
            using var command = Connection.CreateCommand();
            command.CommandText = contributor.SchemaSql;
            command.ExecuteNonQuery();
        }
    }

    public void Dispose() => Connection.Dispose();
}
