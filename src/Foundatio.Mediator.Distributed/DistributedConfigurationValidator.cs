using Foundatio.Messaging;
using Microsoft.Extensions.Hosting;

namespace Foundatio.Mediator.Distributed;

internal sealed class DistributedConfigurationValidator(QueueTopology topology, IMessageExecutionStore? store = null) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (store is null && topology.Queues.Any(queue => queue.Settings.TrackProgress))
            throw new InvalidOperationException("Tracked handlers require Foundatio execution storage. Configure Messaging.UseInMemoryExecutionTracking() or Messaging.UseRedisExecutionTracking().");
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
