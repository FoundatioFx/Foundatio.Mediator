namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Provides queue-specific context to handler methods during message processing.
/// Injected via <see cref="CallContext"/> so handlers can report progress and
/// renew message timeouts for long-running work.
/// </summary>
/// <remarks>
/// Handlers that accept a <see cref="QueueContext"/> parameter will receive it
/// automatically via CallContext injection when processing a queued message:
/// <code>
/// [Queue(Concurrency = 3)]
/// public class LongRunningHandler
/// {
///     public async Task HandleAsync(
///         ProcessLargeFile message,
///         QueueContext queueContext,
///         CancellationToken ct)
///     {
///         foreach (var chunk in GetChunks(message))
///         {
///             await ProcessChunkAsync(chunk, ct);
///             await queueContext.RenewTimeoutAsync(TimeSpan.FromMinutes(5), ct);
///         }
///     }
/// }
/// </code>
/// </remarks>
public class QueueContext
{
    /// <summary>
    /// The name of the queue this message was received from.
    /// </summary>
    public string QueueName { get; init; } = string.Empty;

    /// <summary>
    /// The message type being processed.
    /// </summary>
    public Type? MessageType { get; init; }

    /// <summary>
    /// The number of times this message has been dequeued (including the current attempt).
    /// Useful for detecting poison messages or implementing backoff strategies.
    /// </summary>
    public int DequeueCount { get; init; }

    /// <summary>
    /// The maximum number of retries configured for this queue.
    /// After <c>MaxRetries + 1</c> total attempts, the message will be dead-lettered.
    /// </summary>
    public int MaxRetries { get; init; }

    /// <summary>
    /// When the message was originally enqueued.
    /// </summary>
    public DateTimeOffset EnqueuedAt { get; init; }

    /// <summary>
    /// The unique job identifier for progress tracking, or <c>null</c> if tracking is not enabled.
    /// </summary>
    public string? JobId { get; init; }

    /// <summary>
    /// Delegate invoked by <see cref="ReportProgressAsync(CancellationToken)"/> to signal that the handler
    /// is still actively working. This acts as a heartbeat keep-alive that extends the
    /// message visibility by the configured timeout. Set by the worker infrastructure.
    /// </summary>
    public Func<CancellationToken, Task>? OnReportProgress { get; init; }

    /// <summary>
    /// Delegate invoked by <see cref="ReportProgressAsync(int, string?, CancellationToken)"/>
    /// to update progress percentage and message in the state store.
    /// Set by the worker infrastructure when progress tracking is enabled.
    /// </summary>
    public Func<int, string?, CancellationToken, Task>? OnReportDetailedProgress { get; init; }

    /// <summary>
    /// Delegate invoked by <see cref="RenewTimeoutAsync"/> to extend the message lock
    /// or visibility timeout by a specific duration. Set by the worker infrastructure.
    /// </summary>
    public Func<TimeSpan, CancellationToken, Task>? OnRenewTimeout { get; init; }

    /// <summary>
    /// Reports that the handler is still actively processing the message.
    /// For transports that support it, this extends the visibility timeout
    /// by the configured default duration, preventing the message from being
    /// redelivered during long-running operations.
    /// </summary>
    public Task ReportProgressAsync(CancellationToken cancellationToken = default)
        => OnReportProgress?.Invoke(cancellationToken) ?? Task.CompletedTask;

    /// <summary>
    /// Reports progress with a percentage and optional message.
    /// When progress tracking is enabled, this updates the job state store
    /// and checks for cancellation. If cancellation has been requested,
    /// an <see cref="OperationCanceledException"/> is thrown.
    /// Also acts as a heartbeat to extend the message visibility timeout.
    /// </summary>
    /// <param name="progressPercent">Progress percentage (0–100).</param>
    /// <param name="message">Optional description of current work.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task ReportProgressAsync(int progressPercent, string? message = null, CancellationToken cancellationToken = default)
    {
        // Always renew the visibility timeout as a heartbeat
        if (OnReportProgress is not null)
            await OnReportProgress(cancellationToken).ConfigureAwait(false);

        // Update state store and check for cancellation
        if (OnReportDetailedProgress is not null)
            await OnReportDetailedProgress(progressPercent, message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Extends the message lock or visibility timeout by the specified duration.
    /// Use this for long-running handlers to prevent the message from being
    /// redelivered to another consumer.
    /// </summary>
    public Task RenewTimeoutAsync(TimeSpan extension, CancellationToken cancellationToken = default)
        => OnRenewTimeout?.Invoke(extension, cancellationToken) ?? Task.CompletedTask;
}
