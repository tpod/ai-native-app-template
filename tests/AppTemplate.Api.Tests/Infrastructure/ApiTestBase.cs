namespace AppTemplate.Api.Tests.Infrastructure;

public abstract class ApiTestBase : IAsyncLifetime
{
    protected ApiFactory Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;

    public ValueTask InitializeAsync()
    {
        Factory = new ApiFactory();
        Client = Factory.CreateDefaultClient(new OutputDumpHandler(Factory.Activities));
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        GC.SuppressFinalize(this);
    }

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
