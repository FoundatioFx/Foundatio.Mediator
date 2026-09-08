using Foundatio.Jobs;
using Foundatio.Messaging;
using Microsoft.Extensions.Hosting;

namespace Foundatio.Mediator.Distributed;

internal sealed class DistributedConfigurationValidator(QueueTopology topology, IJobRuntimeStore? store = null) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (store is null && topology.Queues.Any(queue => queue.Settings.TrackProgress))
            throw new InvalidOperationException("Tracked handlers require a Foundatio job store. Configure Jobs.UseInMemory() or Jobs.UseRedis().");
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
