using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// OpenTelemetry metrics emitted by the distributed queue and notification workers.
/// Register the meter with <c>AddMeter(DistributedMetrics.MeterName)</c>.
/// </summary>
public static class DistributedMetrics
{
    /// <summary>
    /// The meter name, <c>Foundatio.Mediator.Distributed</c>.
    /// </summary>
    public const string MeterName = "Foundatio.Mediator.Distributed";

    internal static readonly Meter Meter = new(MeterName);

    internal static readonly Counter<long> Enqueued = Meter.CreateCounter<long>("queue.messages.enqueued", "{message}", "Messages sent to a queue by this process.");
    internal static readonly Counter<long> Processed = Meter.CreateCounter<long>("queue.messages.processed", "{message}", "Messages completed successfully.");
    internal static readonly Counter<long> Failed = Meter.CreateCounter<long>("queue.messages.failed", "{message}", "Messages whose handler failed and were abandoned for retry.");
    internal static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>("queue.messages.dead_lettered", "{message}", "Messages moved to a dead-letter queue.");
    internal static readonly Counter<long> Deferred = Meter.CreateCounter<long>("queue.messages.deferred", "{message}", "Deliveries waiting for a resource lock without consuming a handler retry.");
    internal static readonly Histogram<double> HandlerDuration = Meter.CreateHistogram<double>("queue.handler.duration", "ms", "Handler execution time per message.");
    internal static readonly UpDownCounter<long> InFlight = Meter.CreateUpDownCounter<long>("queue.messages.in_flight", "{message}", "Messages currently being processed by this process.");
    internal static readonly Counter<long> NotificationsPublished = Meter.CreateCounter<long>("notifications.published", "{message}", "Notifications published to the bus.");
    internal static readonly Counter<long> NotificationsReceived = Meter.CreateCounter<long>("notifications.received", "{message}", "Notifications received from the bus and re-published locally.");

    private static readonly ConcurrentDictionary<string, QueueStats> _depth = new(StringComparer.OrdinalIgnoreCase);

    static DistributedMetrics()
    {
        Meter.CreateObservableGauge("queue.depth.visible", () => Snapshot(s => s.ActiveCount), "{message}", "Messages waiting in the queue.");
        Meter.CreateObservableGauge("queue.depth.delayed", () => Snapshot(s => s.DelayedCount), "{message}", "Messages scheduled for later delivery.");
        Meter.CreateObservableGauge("queue.depth.in_flight", () => Snapshot(s => s.InFlightCount), "{message}", "Messages received but not yet completed, across all consumers.");
        Meter.CreateObservableGauge("queue.depth.dead_letter", () => Snapshot(s => s.DeadLetterCount), "{message}", "Messages waiting in the dead-letter queue.");
    }

    internal static void RecordDepth(QueueStats stats) => _depth[stats.QueueName] = stats;

    private static IEnumerable<Measurement<long>> Snapshot(Func<QueueStats, long> select)
    {
        foreach (var (queue, stats) in _depth)
            yield return new Measurement<long>(select(stats), new KeyValuePair<string, object?>("queue", queue));
    }

    internal static TagList Tags(string queue, string? messageType, string? group, string? outcome = null)
    {
        var tags = new TagList
        {
            { "queue", queue }
        };
        if (messageType is not null) tags.Add("message_type", messageType);
        if (group is not null) tags.Add("group", group);
        if (outcome is not null) tags.Add("outcome", outcome);
        return tags;
    }
}
