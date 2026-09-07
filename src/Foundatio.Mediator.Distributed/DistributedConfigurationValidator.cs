using Microsoft.Extensions.Hosting;

namespace Foundatio.Mediator.Distributed;

internal sealed class DistributedConfigurationValidator(IQueueClient client, QueueTopology topology,
    DistributedQueueOptions options, IQueueJobStateStore? stateStore = null) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        if (client.IsDistributed && topology.Queues.Any(queue => queue.Settings.TrackProgress)
            && stateStore?.IsShared != true && !options.AllowProcessLocalJobStateForDevelopment)
            throw new InvalidOperationException("Tracked distributed queues require a shared IQueueJobStateStore. Register UseRedisJobState(), another shared store, or explicitly set AllowProcessLocalJobStateForDevelopment for development/tests.");
        return Task.CompletedTask;
    }
    public Task StartAsync(CancellationToken cancellationToken) => StartingAsync(cancellationToken);
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
