using AppTemplate.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Api.Tests.Infrastructure;

/// <summary>One shared in-memory SQLite database; contexts come and go, the connection keeps the data alive.</summary>
internal sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private IModelContributor[] _contributors = [];

    public SqliteTestDatabase() => _connection.Open();

    public AppDbContext NewContext(params IModelContributor[] contributors)
    {
        if (contributors.Length > 0)
        {
            _contributors = contributors;
        }

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        return new AppDbContext(options, _contributors);
    }

    public void Migrate()
    {
        using var context = NewContext();
        context.Database.Migrate();
        foreach (var contributor in _contributors)
        {
            context.Database.ExecuteSqlRaw(contributor.SchemaSql);
        }
    }

    public void Dispose() => _connection.Dispose();
}
