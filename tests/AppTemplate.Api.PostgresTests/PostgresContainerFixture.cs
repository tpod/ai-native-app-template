using AppTemplate.Api.PostgresTests;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(PostgresContainerFixture))]

namespace AppTemplate.Api.PostgresTests;

/// <summary>
/// One Postgres container for the whole test run; every test gets its own database in it. Needs a container
/// runtime: on this template's machines, rootless Podman with <c>DOCKER_HOST</c> pointing at its socket
/// (<c>scripts/verify.sh</c> sets it).
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    public const string NoRuntimeMessage =
        "No container runtime: start Podman with 'systemctl --user start podman.socket' and set DOCKER_HOST=unix://$XDG_RUNTIME_DIR/podman/podman.sock";

    // The image Aspire 17's AddPostgres uses, so tests and local runs share a major version. Fully qualified:
    // Podman does not resolve short image names without a registries.conf alias.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("docker.io/library/postgres:18.6").Build();

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
    }

    /// <summary>Creates an empty, uniquely named database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }.ConnectionString;
    }

    public ValueTask DisposeAsync() => _container.DisposeAsync();
}
