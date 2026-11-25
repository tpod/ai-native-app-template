using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AppTemplate.Api.Data;

// dotnet-ef needs a provider to scaffold migrations; it does not connect for `migrations add`.
internal sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresAppDbContext>
{
    public PostgresAppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PostgresAppDbContext>()
            .UseNpgsql("Host=localhost;Database=design;Username=postgres;Password=postgres")
            .Options;
        return new PostgresAppDbContext(options, []);
    }
}
