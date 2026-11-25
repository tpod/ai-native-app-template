using AppTemplate.Api.Data;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Actions.Outbox;

/// <summary>
/// Drains the outbox in the background: claims and delivers batches, waits <see cref="OutboxOptions.PollInterval"/>
/// when nothing is claimable, and deletes old delivered rows at most once an hour. Idle when
/// <see cref="OutboxOptions.WorkerEnabled"/> is false (tests drain the outbox themselves).
/// </summary>
internal sealed partial class OutboxWorker(
    OutboxProcessor processor,
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<OutboxOptions> options,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RetentionInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        if (!o.WorkerEnabled)
        {
            return;
        }

        var nextRetention = time.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var claimed = await processor.RunOnceAsync(stoppingToken);
                if (time.GetUtcNow() >= nextRetention)
                {
                    await DeleteOldAsync(o, stoppingToken);
                    nextRetention = time.GetUtcNow() + RetentionInterval;
                }

                if (claimed == 0)
                {
                    await Task.Delay(o.PollInterval, time, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // The worker is a top-level loop: log and keep draining.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogLoopFailed(ex);
                await Task.Delay(o.PollInterval, time, stoppingToken);
            }
        }
    }

    private async Task DeleteOldAsync(OutboxOptions o, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<OutboxStore>();
        await store.DeleteDoneBeforeAsync(time.GetUtcNow().AddDays(-o.RetentionDays), ct);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox worker loop failed; retrying after the poll interval")]
    private partial void LogLoopFailed(Exception exception);
}
