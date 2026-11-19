using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Api.Tests.Infrastructure;

public abstract class ApiTestBase : IAsyncLifetime
{
    protected ApiFactory Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;

    /// <summary>Override to pass constructor arguments (for example fixture flags) to the factory.</summary>
    protected virtual ApiFactory CreateFactory() => new();

    /// <summary>Override to register or replace services; runs before the host builds.</summary>
    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    public ValueTask InitializeAsync()
    {
        Factory = CreateFactory();
        Factory.ConfigureTestServices += ConfigureServices;
        Client = Factory.CreateDefaultClient(new OutputDumpHandler(Factory.Activities));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>POST /api/actions/{action}/preview with the input as the body.</summary>
    protected Task<ApiResponse> Preview(string action, object input) => Post($"/api/actions/{action}/preview", ToObject(input));

    /// <summary>POST /api/actions/{action} with the input fields plus <c>previewToken</c>.</summary>
    protected Task<ApiResponse> Execute(string action, object input, string token)
    {
        var body = ToObject(input);
        body["previewToken"] = token;
        return Post($"/api/actions/{action}", body);
    }

    /// <summary>Previews, then executes with the returned token. Execute is skipped (and returns the preview) when the preview fails.</summary>
    protected async Task<(ApiResponse preview, ApiResponse execute)> PreviewAndExecute(string action, object input)
    {
        var preview = await Preview(action, input);
        if ((int)preview.Status is < 200 or > 299)
        {
            return (preview, preview);
        }

        return (preview, await Execute(action, input, TokenOf(preview)));
    }

    /// <summary>POST /api/queries/{action} with the input as the body.</summary>
    protected Task<ApiResponse> Query(string action, object input) => Post($"/api/queries/{action}", ToObject(input));

    protected static string TokenOf(ApiResponse preview)
        => preview.Body.GetProperty("previewToken").GetString()
            ?? throw new InvalidOperationException($"No previewToken in {preview.Body}");

    /// <summary>The <c>id</c> of the first <c>added</c> change for <paramref name="entity"/>.</summary>
    protected static string IdFromChanges(ApiResponse preview, string entity = "Note")
        => preview.Body.GetProperty("changes").EnumerateArray()
            .Where(c => c.GetProperty("entity").GetString() == entity && c.GetProperty("op").GetString() == "added")
            .Select(c => c.GetProperty("id").GetString())
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"No added {entity} change in {preview.Body}");

    private async Task<ApiResponse> Post(string path, JsonObject body)
    {
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return await ApiResponse.From(await Client.PostAsync(path, content, TestContext.Current.CancellationToken));
    }

    private static JsonObject ToObject(object input)
        => JsonSerializer.SerializeToNode(input, JsonSerializerOptions.Web) as JsonObject
            ?? throw new ArgumentException("Input must serialize to a JSON object.", nameof(input));

    /// <summary>After every request, writes the span dump and response body to the test output (shown when the test fails).</summary>
    private sealed class OutputDumpHandler(ActivityCapture activities) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var traceId = response.Headers.TryGetValues("X-Trace-Id", out var v) ? v.FirstOrDefault() ?? "" : "";
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{request.Method} {request.RequestUri?.PathAndQuery} -> {(int)response.StatusCode}\n{activities.Dump(traceId)}{body}");
            return response;
        }
    }
}
