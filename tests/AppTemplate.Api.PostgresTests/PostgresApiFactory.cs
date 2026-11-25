using AppTemplate.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AppTemplate.Api.PostgresTests;

/// <summary>
/// The real app on a real Postgres database: Program's own <c>UseNpgsql</c> registration, no service replacement.
/// Only the connection string and the preview signing key come from the test.
/// </summary>
public sealed class PostgresApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    private const string SigningKey = "test-signing-key-0123456789abcdef-0123";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" keeps Program's Development-only Migrate from running; CreateHost migrates instead.
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:appdb", connectionString);
        builder.UseSetting("Preview:SigningKey", SigningKey);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
        return host;
    }
}
