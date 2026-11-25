using AppTemplate.Api.Data;
using AppTemplate.Api.PostgresTests;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgresContainerFixture))]

namespace AppTemplate.Api.PostgresTests;

/// <summary>
/// One Postgres container for the whole test run. The migrations run once, into a template database; every test
/// gets its own database cloned from it (<c>CREATE DATABASE … TEMPLATE</c>), so tests are isolated, can run in
/// parallel and never pay for migrations. Needs a container runtime: on this template's machines, rootless Podman
/// with <c>DOCKER_HOST</c> pointing at its socket (<c>scripts/verify.sh</c> sets it).
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    public const string NoRuntimeMessage =
        "No container runtime: start Podman with 'systemctl --user start podman.socket' and set DOCKER_HOST=unix://$XDG_RUNTIME_DIR/podman/podman.sock";

    private const string TemplateName = "app_template";

    // The image Aspire 17's AddPostgres uses, so tests and local runs share a major version. Fully qualified:
    // Podman does not resolve short image names without a registries.conf alias.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("docker.io/library/postgres:18.6").Build();

    // Postgres rejects two clones of one template at the same moment ("source database is being accessed by other
    // users"), so clones are taken one at a time; each takes tens of milliseconds.
    private readonly SemaphoreSlim _cloneLock = new(1, 1);

    public async ValueTask InitializeAsync()
    {
        try
        {
            await _container.StartAsync(TestContext.Current.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(NoRuntimeMessage, ex);
        }

        await CreateTemplateAsync();
    }

    /// <summary>Creates a uniquely named database with every migration applied, cloned from the run's template.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        await _cloneLock.WaitAsync(TestContext.Current.CancellationToken);
        try
        {
            return await CreateAsync($"TEMPLATE \"{TemplateName}\"");
        }
        finally
        {
            _cloneLock.Release();
        }
    }

    /// <summary>Creates a uniquely named empty database, for tests of the migrations themselves.</summary>
    public Task<string> CreateEmptyDatabaseAsync() => CreateAsync(options: null);

    public async ValueTask DisposeAsync()
    {
        _cloneLock.Dispose();
        await _container.DisposeAsync();
    }

    private async Task CreateTemplateAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await ExecuteAsync($"CREATE DATABASE \"{TemplateName}\"", ct);

        var options = new DbContextOptionsBuilder<PostgresAppDbContext>().UseNpgsql(ConnectionStringFor(TemplateName)).Options;
        await using (var db = new PostgresAppDbContext(options, []))
        {
            await db.Database.MigrateAsync(ct);
        }

        // A template cannot be cloned while any session is connected to it, and pooled connections stay open.
        NpgsqlConnection.ClearAllPools();
        await ExecuteAsync($"ALTER DATABASE \"{TemplateName}\" WITH IS_TEMPLATE true ALLOW_CONNECTIONS false", ct);
    }

    private async Task<string> CreateAsync(string? options)
    {
        var name = $"t_{Guid.NewGuid():N}";
        await ExecuteAsync($"CREATE DATABASE \"{name}\" {options}", TestContext.Current.CancellationToken);
        return ConnectionStringFor(name);
    }

    // Admin statements run on the container's default database, never on the template or a test database.
    private async Task ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_container.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    private string ConnectionStringFor(string database)
        => new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = database }.ConnectionString;
}
