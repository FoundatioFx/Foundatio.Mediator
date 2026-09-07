using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Samples queue depth from the transport on an interval and publishes it through the
/// <c>queue.depth.*</c> observable gauges.
/// </summary>
internal sealed class QueueDepthMetricsService(
    IQueueClient client,
    QueueTopology topology,
    DistributedQueueOptions options,
    ILogger<QueueDepthMetricsService> logger,
    DistributedInfrastructureReady? infraReady = null,
    TimeProvider? timeProvider = null) : BackgroundService
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.QueueDepthPollInterval <= TimeSpan.Zero || topology.Queues.Count == 0)
            return;

        if (infraReady is not null)
        {
            try { await infraReady.WaitAsync(stoppingToken).ConfigureAwait(false); }
            catch (Exception) { return; }
        }

        var queueNames = topology.Queues.Select(q => q.QueueName).ToList();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var stats = await client.GetQueueStatsAsync(queueNames, stoppingToken).ConfigureAwait(false);
                foreach (var stat in stats)
                    DistributedMetrics.RecordDepth(stat);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to sample queue depth");
            }

            try
            {
                await Task.Delay(options.QueueDepthPollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
