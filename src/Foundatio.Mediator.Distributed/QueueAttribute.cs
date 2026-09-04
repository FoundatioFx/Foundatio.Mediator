using Foundatio.Mediator;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Routes a handler's messages through a durable queue instead of running them inline.
/// Invoking the handler enqueues the message and returns <see cref="Result.Accepted()"/>; a
/// <see cref="QueueWorker"/> runs the handler through the normal middleware pipeline.
/// </summary>
[UseMiddleware(typeof(QueueMiddleware))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class QueueAttribute : Attribute
{
    /// <summary>
    /// Queue name. Defaults to the message type name. Handlers sharing a name share one queue and one
    /// worker and must declare identical settings.
    /// </summary>
    public string? QueueName { get; set; }

    /// <summary>
    /// Total attempts before dead-lettering: one initial attempt plus retries. Must be at least 1;
    /// a negative value retries without limit.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Visibility timeout in seconds. Renewed automatically at two thirds while the handler runs when
    /// <see cref="AutoRenewTimeout"/> is on.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Messages processed concurrently per worker instance.
    /// </summary>
    public int Concurrency { get; set; } = 1;

    /// <summary>
    /// Messages fetched per receive. Defaults to <see cref="Concurrency"/>.
    /// </summary>
    public int PrefetchCount { get; set; }

    /// <summary>
    /// Worker group name. <see cref="DistributedQueueOptions.Workers"/> selects workers by group or queue
    /// name, so grouping related handlers lets a process run or skip them together.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Whether the worker completes or abandons the message from the handler's result.
    /// </summary>
    public bool AutoComplete { get; set; } = true;

    /// <summary>
    /// Whether the worker renews the visibility timeout while the handler runs.
    /// </summary>
    public bool AutoRenewTimeout { get; set; } = true;

    /// <summary>
    /// Delay strategy between retries.
    /// </summary>
    public QueueRetryPolicy RetryPolicy { get; set; } = QueueRetryPolicy.Exponential;

    /// <summary>
    /// Base delay in seconds for <see cref="QueueRetryPolicy.Fixed"/> and <see cref="QueueRetryPolicy.Exponential"/>.
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 5;

    /// <summary>
    /// Explicit delays for <see cref="QueueRetryPolicy.Schedule"/>, for example <c>"5s,1m,15m,30m"</c>.
    /// Setting this selects the schedule policy.
    /// </summary>
    public string? RetryDelays { get; set; }

    /// <summary>
    /// Tracks job state and progress in the <see cref="IQueueJobStateStore"/>.
    /// </summary>
    public bool TrackProgress { get; set; }

    /// <summary>
    /// Human-readable description shown by queue administration tooling.
    /// </summary>
    public string? Description { get; set; }
}
