using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Api.PostgresTests;

/// <summary>
/// The real app on a real Postgres database: Program's own <c>UseNpgsql</c> registration, no service replacement.
/// Only the connection string, the preview signing key and optional extra services come from the test. The database
/// arrives already migrated (<see cref="PostgresContainerFixture.CreateDatabaseAsync"/> clones it from the run's
/// template). The outbox worker stays idle so tests drain deterministically.
/// </summary>
public sealed class PostgresApiFactory(string connectionString, Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    private const string SigningKey = "test-signing-key-0123456789abcdef-0123";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" keeps Program's Development-only Migrate from running; the template is already migrated.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:appdb", connectionString);
        builder.UseSetting("Preview:SigningKey", SigningKey);
        builder.UseSetting("Outbox:WorkerEnabled", "false");
        if (configureServices is not null)
        {
            builder.ConfigureTestServices(configureServices);
        }
    }
}
