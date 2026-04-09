namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Describes a registered queue worker's configuration and provides access to runtime statistics.
/// Configuration properties are immutable after initialization.
/// Exposed via <see cref="IQueueWorkerRegistry"/> for dashboard and monitoring.
/// </summary>
public sealed class QueueWorkerInfo
{
    /// <summary>
    /// The name of the queue this worker processes.
    /// </summary>
    public required string QueueName { get; init; }

    /// <summary>
    /// The full name of the message type.
    /// </summary>
    public required string MessageTypeName { get; init; }

    /// <summary>
    /// Number of concurrent consumer tasks.
    /// </summary>
    public int Concurrency { get; init; }

    /// <summary>
    /// Number of messages fetched per receive batch.
    /// </summary>
    public int PrefetchCount { get; init; }

    /// <summary>
    /// Maximum number of attempts before dead-lettering.
    /// </summary>
    public int MaxAttempts { get; init; }

    /// <summary>
    /// Message visibility timeout.
    /// </summary>
    public TimeSpan VisibilityTimeout { get; init; }

    /// <summary>
    /// Queue group for selective hosting.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// The retry delay strategy.
    /// </summary>
    public QueueRetryPolicy RetryPolicy { get; init; }

    /// <summary>
    /// Whether job progress tracking is enabled.
    /// </summary>
    public bool TrackProgress { get; init; }

    /// <summary>
    /// A human-readable description of the queue.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Runtime statistics for this worker. Updated atomically by the worker during message processing.
    /// </summary>
    public QueueWorkerStats Stats { get; } = new();
}
