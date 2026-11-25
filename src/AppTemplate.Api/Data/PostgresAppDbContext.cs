using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Api.Data;

/// <summary>The runtime database. Migrations live in <c>Data/Migrations/Postgres</c>; add them with <c>scripts/add-migration.sh</c>.</summary>
public sealed class PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options, IEnumerable<IModelContributor> contributors)
    : AppDbContext(options, contributors)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        CaseInsensitiveText.ApplyForPostgres(modelBuilder);
    }
}
