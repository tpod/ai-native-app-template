using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AppTemplate.Api.Actions.Telemetry;

/// <summary>Delivery counter by outcome and delivery lag (commit to done), on the actions meter.</summary>
public sealed class OutboxMetrics
{
    private readonly Counter<long> _deliveries;
    private readonly Histogram<double> _lag;

    public OutboxMetrics(IMeterFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var meter = factory.Create(ActionTelemetry.Name);
        _deliveries = meter.CreateCounter<long>("outbox.deliveries", description: "Outbox delivery attempts by outcome.");
        _lag = meter.CreateHistogram<double>("outbox.delivery.lag", unit: "ms", description: "Time from commit to successful delivery.");
    }

    public void Record(string outcome, TimeSpan? lag)
    {
        _deliveries.Add(1, new TagList { { OutboxTelemetry.TagOutcome, outcome } });
        if (lag is { } value)
        {
            _lag.Record(value.TotalMilliseconds);
        }
    }
}
