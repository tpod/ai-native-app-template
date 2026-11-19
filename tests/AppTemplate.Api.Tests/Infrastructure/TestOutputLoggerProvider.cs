using Microsoft.Extensions.Logging;
using Xunit.Sdk;

namespace AppTemplate.Api.Tests.Infrastructure;

/// <summary>Routes host logs to the xUnit test output, which is shown when a test fails.</summary>
public sealed class TestOutputLoggerProvider(ITestOutputHelper? output) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new OutputLogger(output, categoryName);

    public void Dispose()
    {
    }

    private sealed class OutputLogger(ITestOutputHelper? output, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => output is not null && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (output is null)
            {
                return;
            }

            try
            {
                output.WriteLine($"{logLevel,-11} {category}: {formatter(state, exception)}{(exception is null ? "" : Environment.NewLine + exception)}");
            }
            catch (InvalidOperationException)
            {
                // The test already finished; late log lines from background work are dropped.
            }
        }
    }
}
