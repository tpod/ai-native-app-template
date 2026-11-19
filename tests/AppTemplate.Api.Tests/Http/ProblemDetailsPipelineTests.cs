using System.Net;
using AppTemplate.Api.Actions.Http;
using AppTemplate.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;

namespace AppTemplate.Api.Tests.Http;

/// <summary>The same pipeline Program uses, on a tiny host with routes that provoke framework-generated and unhandled errors.</summary>
public sealed class ProblemDetailsPipelineTests : IAsyncLifetime
{
    private readonly CapturingLoggerProvider _logs = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);
        builder.Services.AddAppProblemDetails();
        _app = builder.Build();
        _app.UseAppProblemDetails();
        _app.MapGet("/get-only", () => "ok");
        _app.MapGet("/boom", IResult () => throw new InvalidOperationException("secret connection string"));
        await _app.StartAsync(TestContext.Current.CancellationToken);
        _client = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Get_ThrowingRoute_LogsExactlyOneErrorWithCodeAndTraceId()
    {
        var r = await ApiResponse.From(await _client.GetAsync("/boom", TestContext.Current.CancellationToken));

        var errors = _logs.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        errors.Count.ShouldBe(1, string.Join(" | ", errors.Select(e => e.Category + ": " + e.Message)));
        errors[0].Message.ShouldContain("internal.unexpected");
        errors[0].Message.ShouldContain(r.TraceId);
        errors[0].Exception.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task Post_GetOnlyRoute_Returns405ProblemWithMalformedCode()
    {
        var r = await ApiResponse.From(await _client.PostAsync("/get-only", null, TestContext.Current.CancellationToken));
        r.ShouldFailWith("request.malformed", HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task Get_UnknownRoute_Returns404ProblemWithNotFoundCode()
    {
        var r = await ApiResponse.From(await _client.GetAsync("/nope", TestContext.Current.CancellationToken));
        r.ShouldFailWith("request.not_found", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_ThrowingRoute_Returns500ProblemWithoutExceptionText()
    {
        var r = await ApiResponse.From(await _client.GetAsync("/boom", TestContext.Current.CancellationToken));
        r.ShouldFailWith("internal.unexpected", HttpStatusCode.InternalServerError);
        r.Body.ToString().ShouldNotContain("secret connection string");
        r.TraceId.ShouldNotBeNullOrWhiteSpace();
    }
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyCollection<LogEntry> Entries => _entries;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    internal sealed record LogEntry(string Category, LogLevel Level, string Message, Exception? Exception);

    private sealed class CapturingLogger(string category, System.Collections.Concurrent.ConcurrentQueue<LogEntry> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => sink.Enqueue(new LogEntry(category, logLevel, formatter(state, exception), exception));
    }
}
