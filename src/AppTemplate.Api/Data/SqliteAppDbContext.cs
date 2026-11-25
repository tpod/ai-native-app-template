using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Api.Data;

/// <summary>
/// The fast test suite's database (in-memory SQLite with real migrations). Never registered by the app itself.
/// Migrations live in <c>Data/Migrations/Sqlite</c>; add them with <c>scripts/add-migration.sh</c>.
/// </summary>
public sealed class SqliteAppDbContext(DbContextOptions<SqliteAppDbContext> options, IEnumerable<IModelContributor> contributors)
    : AppDbContext(options, contributors)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        CaseInsensitiveText.ApplyForSqlite(modelBuilder);
    }
}
