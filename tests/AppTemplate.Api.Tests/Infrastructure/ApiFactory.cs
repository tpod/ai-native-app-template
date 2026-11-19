using AppTemplate.Api.Data;
using AppTemplate.Api.Tests.Fixtures.Notes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace AppTemplate.Api.Tests.Infrastructure;

/// <param name="includeFixtures">Registers the Notes fixture (handlers, validators, event recorder, model contributor). Off for hosts that must see only the real app.</param>
public sealed class ApiFactory(bool includeFixtures = true) : WebApplicationFactory<Program>, IAsyncDisposable
{
    public const string SigningKey = "test-signing-key-0123456789abcdef-0123";

    private readonly SqliteTestDatabase _database = new();
    private readonly ITestOutputHelper? _output = TestContext.Current.TestOutputHelper;

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public ActivityCapture Activities { get; } = new();

    /// <summary>Lets a later task register extra services (for example an <see cref="IModelContributor"/>) before the host builds.</summary>
    public Action<IServiceCollection>? ConfigureTestServices { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" keeps Program's Development-only Migrate from running; this factory owns migration.
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Preview:SigningKey"] = SigningKey,
        }));
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(new TestOutputLoggerProvider(_output));
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddScoped(_ => new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_database.Connection).Options);
            services.AddScoped<DbContextOptions>(sp => sp.GetRequiredService<DbContextOptions<AppDbContext>>());

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
