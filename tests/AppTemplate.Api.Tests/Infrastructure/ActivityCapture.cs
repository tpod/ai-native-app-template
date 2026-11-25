using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace AppTemplate.Api.Tests.Infrastructure;

/// <summary>Records finished activities from the app's sources so a failing test can print the span tree of a request.</summary>
public sealed class ActivityCapture : IDisposable
{
    private static readonly HashSet<string> SourceNames = ["AppTemplate.Actions", "Microsoft.AspNetCore"];

    private readonly ConcurrentQueue<Activity> _activities = new();
    private readonly ActivityListener _listener;

    public ActivityCapture()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => SourceNames.Contains(source.Name),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _activities.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Every captured span, from any test running in this process; filter by something unique to yours.</summary>
    public IReadOnlyList<Activity> All => [.. _activities];

    public IReadOnlyList<Activity> ForTrace(string traceId)
        => [.. _activities.Where(a => a.TraceId.ToString() == traceId).OrderBy(a => a.StartTimeUtc)];

    public string Dump(string traceId)
    {
        var spans = ForTrace(traceId);
        var sb = new StringBuilder().AppendLine($"trace {traceId}: {spans.Count} span(s)");
        foreach (var a in spans)
        {
            sb.AppendLine($"  [{a.Source.Name}] {a.DisplayName} ({a.Duration.TotalMilliseconds:F1} ms, {a.Status})");
            foreach (var tag in a.TagObjects)
            {
                sb.AppendLine($"      {tag.Key} = {tag.Value}");
            }
        }

        return sb.ToString();
    }

    public void Dispose() => _listener.Dispose();
}
