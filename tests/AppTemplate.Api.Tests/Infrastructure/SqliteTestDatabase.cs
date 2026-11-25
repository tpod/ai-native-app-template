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
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(Connection).Options;
        return new SqliteAppDbContext(options, contributors);
    }

    /// <summary>Applies EF migrations with the base model, then runs each contributor's raw DDL.</summary>
    public void Migrate(params IModelContributor[] contributors) => Migrate((IEnumerable<IModelContributor>)contributors);

    public void Migrate(IEnumerable<IModelContributor> contributors)
    {
        // Base model only: contributor entities are not in the migration snapshot and would trip the
        // pending-model-changes check. Their tables come from SchemaSql instead.
        using (var context = NewContext())
        {
            context.Database.Migrate();
        }

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
