using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Hosted service that pre-creates all queues and topics under a single trace span
/// so startup infrastructure calls are grouped rather than appearing as individual root traces.
/// Registered before queue/notification workers so resources exist before polling begins.
/// </summary>
internal sealed class DistributedInfrastructureInitializer(
    IQueueClient? queueClient,
    IPubSubClient? pubSubClient,
    DistributedInfrastructureOptions options,
    ILogger<DistributedInfrastructureInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.QueueNames.Count == 0 && options.TopicNames.Count == 0)
            return;

        using var activity = MediatorActivitySource.Instance.StartActivity("Mediator Infrastructure Setup");

        if (options.QueueNames.Count > 0 && queueClient is not null)
        {
            logger.LogInformation("Ensuring {Count} queue(s) exist: {Queues}", options.QueueNames.Count, options.QueueNames);
            await queueClient.EnsureQueuesAsync(options.QueueNames, cancellationToken).ConfigureAwait(false);
        }

        if (options.TopicNames.Count > 0 && pubSubClient is not null)
        {
            logger.LogInformation("Ensuring {Count} topic(s) exist: {Topics}", options.TopicNames.Count, options.TopicNames);
            await pubSubClient.EnsureTopicsAsync(options.TopicNames, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Collects queue and topic names during service registration for use by
/// <see cref="DistributedInfrastructureInitializer"/> at startup.
/// </summary>
internal sealed class DistributedInfrastructureOptions
{
    public List<string> QueueNames { get; } = [];
    public List<string> TopicNames { get; } = [];
}
