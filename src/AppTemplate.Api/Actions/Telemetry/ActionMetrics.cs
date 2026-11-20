using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AppTemplate.Api.Actions.Telemetry;

/// <summary>Call counter and duration histogram for action phases, tagged by name, phase and outcome.</summary>
public sealed class ActionMetrics
{
    private readonly Counter<long> _calls;
    private readonly Histogram<double> _duration;

    public ActionMetrics(IMeterFactory factory)
    {
        var meter = factory.Create(ActionTelemetry.Name);
        _calls = meter.CreateCounter<long>("app.actions.calls", description: "Action phase calls, including failures.");
        _duration = meter.CreateHistogram<double>("app.actions.duration", unit: "s", description: "Duration of action phase calls.");
    }

    public void Record(string name, string phase, string outcome, TimeSpan elapsed)
    {
        var tags = new TagList
        {
            { ActionTelemetry.TagName, name },
            { ActionTelemetry.TagPhase, phase },
            { ActionTelemetry.TagOutcome, outcome },
        };
        _calls.Add(1, tags);
        _duration.Record(elapsed.TotalSeconds, tags);
    }
}
