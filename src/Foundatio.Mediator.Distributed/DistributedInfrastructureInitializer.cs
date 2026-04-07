using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Hosted service that pre-creates all queues and topics in the background.
/// Workers and publishers await <see cref="DistributedInfrastructureReady.WaitAsync"/>
/// before using infrastructure, so the app can start accepting requests immediately.
/// </summary>
internal sealed class DistributedInfrastructureInitializer(
    IQueueClient? queueClient,
    IPubSubClient? pubSubClient,
    DistributedInfrastructureOptions options,
    DistributedInfrastructureReady ready,
    ILogger<DistributedInfrastructureInitializer> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.QueueNames.Count == 0 && options.TopicNames.Count == 0)
        {
            ready.SetReady();
            return Task.CompletedTask;
        }

        // Fire and forget — workers await ready.WaitAsync() before polling
        _ = InitializeAsync(cancellationToken);
        return Task.CompletedTask;
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            using var activity = MediatorActivitySource.Instance.StartActivity("Mediator Infrastructure Setup");

            // Warm up the transport connections with one real call per transport.
            // The first AWS SDK call absorbs DNS resolution, TLS handshake, and
            // endpoint discovery (~20s against cold LocalStack). Creating one queue
            // and one topic first means the parallel batch below gets warm connections.
            var warmUpTasks = new List<Task>(2);
            if (options.QueueNames.Count > 0 && queueClient is not null)
                warmUpTasks.Add(queueClient.EnsureQueuesAsync([options.QueueNames[0]], cancellationToken));
            if (options.TopicNames.Count > 0 && pubSubClient is not null)
                warmUpTasks.Add(pubSubClient.EnsureTopicsAsync([options.TopicNames[0]], cancellationToken));
            await Task.WhenAll(warmUpTasks).ConfigureAwait(false);
            logger.LogInformation("Transport connections warm in {ElapsedMs}ms", sw.ElapsedMilliseconds);

            // Now create the remaining queues/topics with warm connections
            var tasks = new List<Task>(2);

            if (options.QueueNames.Count > 1 && queueClient is not null)
            {
                var remaining = options.QueueNames.Skip(1).ToList();
                logger.LogInformation("Ensuring remaining {Count} queue(s) exist: {Queues}", remaining.Count, remaining);
                tasks.Add(queueClient.EnsureQueuesAsync(remaining, cancellationToken));
            }

            // Topic was already fully set up (queue + subscribe) during warm-up
            // so we only need to process additional topics if there are more than one.
            if (options.TopicNames.Count > 1 && pubSubClient is not null)
            {
                var remaining = options.TopicNames.Skip(1).ToList();
                logger.LogInformation("Ensuring remaining {Count} topic(s) exist: {Topics}", remaining.Count, remaining);
                tasks.Add(pubSubClient.EnsureTopicsAsync(remaining, cancellationToken));
            }

            if (tasks.Count > 0)
                await Task.WhenAll(tasks).ConfigureAwait(false);

            logger.LogInformation("Distributed infrastructure ready in {ElapsedMs}ms", sw.ElapsedMilliseconds);
            ready.SetReady();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize distributed infrastructure");
            ready.SetFailed(ex);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// Signals that distributed infrastructure (queues, topics) has been created.
/// Workers await <see cref="WaitAsync"/> before starting their polling loops.
/// </summary>
public sealed class DistributedInfrastructureReady
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Blocks until infrastructure is ready or throws if initialization failed.
    /// </summary>
    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        if (_tcs.Task.IsCompleted)
            return _tcs.Task;

        return WaitWithCancellationAsync(cancellationToken);
    }

    private async Task WaitWithCancellationAsync(CancellationToken cancellationToken)
    {
        var cancelTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var reg = cancellationToken.Register(() => cancelTcs.TrySetCanceled(cancellationToken));
        await Task.WhenAny(_tcs.Task, cancelTcs.Task).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await _tcs.Task.ConfigureAwait(false); // propagate any failure
    }

    internal void SetReady() => _tcs.TrySetResult();

    internal void SetFailed(Exception ex) => _tcs.TrySetException(ex);
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
