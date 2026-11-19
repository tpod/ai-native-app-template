using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AppTemplate.Api.Tests.Infrastructure;

public sealed record ApiResponse(HttpStatusCode Status, JsonElement Body, HttpResponseHeaders Headers, string? ContentType = null)
{
    public string TraceId => Headers.TryGetValues("X-Trace-Id", out var values) ? values.FirstOrDefault() ?? "" : "";

    public static async Task<ApiResponse> From(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType;
        var isJson = contentType is not null && (contentType.EndsWith("/json", StringComparison.Ordinal) || contentType.EndsWith("+json", StringComparison.Ordinal));
        var body = isJson && !string.IsNullOrWhiteSpace(text) ? JsonDocument.Parse(text).RootElement.Clone() : default;
        return new ApiResponse(response.StatusCode, body, response.Headers, contentType);
    }
}
