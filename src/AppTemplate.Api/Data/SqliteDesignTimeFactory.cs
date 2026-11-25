using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AppTemplate.Api.Data;

// dotnet-ef needs a provider to scaffold migrations; it does not connect for `migrations add`.
internal sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteAppDbContext>
{
    public SqliteAppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite("Data Source=design.db").Options;
        return new SqliteAppDbContext(options, []);
    }
}
