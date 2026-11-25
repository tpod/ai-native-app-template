using AppTemplate.Api.Data;
using AppTemplate.Api.Tests.Fixtures.Notes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace AppTemplate.Api.Tests.Infrastructure;

/// <param name="includeFixtures">Registers the Notes fixture (handlers, validators, event recorder, model contributor). Off for hosts that must see only the real app.</param>
/// <param name="webRoot">When set, the host serves static files and the SPA fallback from this directory.</param>
public sealed class ApiFactory(bool includeFixtures = true, string? webRoot = null) : WebApplicationFactory<Program>, IAsyncDisposable
{
    public const string SigningKey = "test-signing-key-0123456789abcdef-0123";

    private readonly SqliteTestDatabase _database = new();
    private readonly ITestOutputHelper? _output = TestContext.Current.TestOutputHelper;

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public ActivityCapture Activities { get; } = new();

    /// <summary>Configuration keys added to the host (in-memory, highest priority). Change them before the host builds.</summary>
    public Dictionary<string, string?> Settings { get; } = new() { ["Preview:SigningKey"] = SigningKey };

    /// <summary>Lets a later task register extra services (for example an <see cref="IModelContributor"/>) before the host builds.</summary>
    public Action<IServiceCollection>? ConfigureTestServices { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" keeps Program's Development-only Migrate from running; this factory owns migration.
        builder.UseEnvironment("Testing");
        if (webRoot is not null)
        {
            builder.UseWebRoot(webRoot);
        }

        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new TestOutputLoggerProvider(_output));
        });
        builder.ConfigureServices(services =>
        {
            // The app registers Postgres; the fast suite swaps in SQLite. Remove every piece of the Postgres
            // registration, including EF 10's IDbContextOptionsConfiguration, so Npgsql is never configured.
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<PostgresAppDbContext>();
            services.RemoveAll<DbContextOptions<PostgresAppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PostgresAppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddScoped(_ => new DbContextOptionsBuilder<SqliteAppDbContext>().UseSqlite(_database.Connection).Options);
            services.AddScoped<DbContextOptions>(sp => sp.GetRequiredService<DbContextOptions<SqliteAppDbContext>>());
            services.AddScoped<AppDbContext, SqliteAppDbContext>();

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);

            if (includeFixtures)
            {
                NoteFixture.Register(services);
            }

            ConfigureTestServices?.Invoke(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        _database.Migrate(scope.ServiceProvider.GetServices<IModelContributor>());
        return host;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        Activities.Dispose();
        _database.Dispose();
        GC.SuppressFinalize(this);
    }
}
