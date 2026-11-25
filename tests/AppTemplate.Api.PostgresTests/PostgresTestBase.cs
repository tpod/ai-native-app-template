using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using AppTemplate.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Api.PostgresTests;

/// <summary>
/// A fresh database and host per test, like <c>ApiTestBase</c> in the fast suite, and the same HTTP routes
/// (AGENTS.md, "Preview protocol").
/// </summary>
public abstract class PostgresTestBase(PostgresContainerFixture postgres) : IAsyncLifetime
{
    private string _connectionString = null!;
    private PostgresApiFactory _factory = null!;

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected HttpClient Client { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _connectionString = await postgres.CreateDatabaseAsync();
        _factory = new PostgresApiFactory(_connectionString);
        Client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>A context on this test's database, outside the app's DI scope.</summary>
    protected PostgresAppDbContext NewContext()
        => new(new DbContextOptionsBuilder<PostgresAppDbContext>().UseNpgsql(_connectionString).Options, []);

    protected Task<(HttpStatusCode Status, JsonElement Body)> Preview(string action, object input)
        => Post($"/api/actions/{action}/preview", ToObject(input));

    protected Task<(HttpStatusCode Status, JsonElement Body)> Query(string name, object input)
        => Post($"/api/queries/{name}", ToObject(input));

    /// <summary>Previews and executes; fails the test if either step does not succeed.</summary>
    protected async Task<JsonElement> PreviewAndExecute(string action, object input)
    {
        var (previewStatus, preview) = await Preview(action, input);
        if (previewStatus != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Preview of {action} returned {(int)previewStatus}: {preview}");
        }

        var body = ToObject(input);
        body["previewToken"] = preview.GetProperty("previewToken").GetString();
        var (status, result) = await Post($"/api/actions/{action}", body);
        return status == HttpStatusCode.OK
            ? result
            : throw new InvalidOperationException($"Execute of {action} returned {(int)status}: {result}");
    }

    private async Task<(HttpStatusCode, JsonElement)> Post(string path, JsonObject body)
    {
        using var response = await Client.PostAsJsonAsync(path, body, Ct);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (response.StatusCode, json);
    }

    private static JsonObject ToObject(object input)
        => JsonSerializer.SerializeToNode(input, JsonSerializerOptions.Web) as JsonObject
            ?? throw new ArgumentException("Input must serialize to a JSON object.", nameof(input));
}
