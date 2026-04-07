using Foundatio.Mediator;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Marks a handler class or method for queue-based processing.
/// When applied, invocations via <c>mediator.InvokeAsync()</c> will serialize the message
/// and send it to a queue for asynchronous processing instead of executing the handler inline.
/// </summary>
/// <example>
/// <code>
/// [Queue(Concurrency = 3)]
/// public class OrderProcessingHandler
/// {
///     public async Task&lt;Result&gt; HandleAsync(
///         ProcessOrder message,
///         CancellationToken ct)
///     {
///         // ... do work ...
///         return Result.Success();
///     }
/// }
/// </code>
/// </example>
[UseMiddleware(typeof(QueueMiddleware))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class QueueAttribute : Attribute
{
    /// <summary>
    /// Override the queue name. Defaults to the message type name.
    /// </summary>
    public string? QueueName { get; set; }

    /// <summary>
    /// Maximum number of times the message will be attempted before dead-lettering.
    /// Default is 3 (1 initial attempt + 2 retries).
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Work item timeout in seconds.
    /// The timeout auto-renews on a background timer unless <see cref="AutoRenewTimeout"/> is disabled,
    /// in which case the message is automatically abandoned if not completed within this duration.
    /// Default is 30.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Number of concurrent consumer tasks processing this queue. Default is 1.
    /// </summary>
    public int Concurrency { get; set; } = 1;

    /// <summary>
    /// Number of messages to fetch per receive batch.
    /// When 0 (the default), automatically matches <see cref="Concurrency"/>
    /// so each receive call can fill the consumer pipeline in a single round-trip.
    /// </summary>
    public int PrefetchCount { get; set; }

    /// <summary>
    /// Queue group name for selective hosting. When set, only workers configured
    /// for the matching group will process messages from this queue.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// When true, the worker automatically completes the message on success
    /// and abandons it on exception. When false, the handler must call
    /// <see cref="QueueContext.CompleteAsync"/> or <see cref="QueueContext.AbandonAsync(CancellationToken)"/>
    /// explicitly. Default is true.
    /// </summary>
    public bool AutoComplete { get; set; } = true;

    /// <summary>
    /// When true, the worker automatically renews the message visibility timeout
    /// on a background timer, preventing the message from being redelivered while
    /// the handler is still processing. When false, the handler must call
    /// <see cref="QueueContext.RenewTimeoutAsync"/> manually for long-running work.
    /// Default is true.
    /// </summary>
    public bool AutoRenewTimeout { get; set; } = true;

    /// <summary>
    /// The retry delay strategy for failed messages. Default is <see cref="QueueRetryPolicy.Exponential"/>.
    /// </summary>
    public QueueRetryPolicy RetryPolicy { get; set; } = QueueRetryPolicy.Exponential;

    /// <summary>
    /// The base delay between retries in seconds.
    /// For <see cref="QueueRetryPolicy.Fixed"/>, this is the constant delay.
    /// For <see cref="QueueRetryPolicy.Exponential"/>, this is the initial delay that doubles on each retry.
    /// Default is 5.
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 5;

    /// <summary>
    /// When true, the job's progress and state are tracked via <see cref="IQueueJobStateStore"/>.
    /// Enables progress reporting, cancellation, and dashboard visibility. Default is false.
    /// </summary>
    public bool TrackProgress { get; set; }

    /// <summary>
    /// A human-readable description of the queue, shown in the dashboard tooltip.
    /// </summary>
    public string? Description { get; set; }
}
