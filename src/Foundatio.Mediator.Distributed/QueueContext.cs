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
/// When <c>AutoComplete</c> is disabled, the handler is responsible for completing
/// or abandoning the message explicitly:
/// <code>
/// [Queue(AutoComplete = false)]
/// public class ManualAckHandler
/// {
///     public async Task HandleAsync(
///         ProcessPayment message,
///         QueueContext queueContext,
///         CancellationToken ct)
///     {
///         try
///         {
///             await ChargeAsync(message, ct);
///             await queueContext.CompleteAsync(ct);
///         }
///         catch (TransientException)
///         {
///             await queueContext.AbandonAsync(TimeSpan.FromSeconds(30), ct);
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
    /// The maximum number of attempts configured for this queue.
    /// After this many attempts, the message will be dead-lettered.
    /// </summary>
    public int MaxAttempts { get; init; }

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
    internal Func<CancellationToken, Task>? OnReportProgress { get; init; }

    /// <summary>
    /// Delegate invoked by <see cref="ReportProgressAsync(int, string?, CancellationToken)"/>
    /// to update progress percentage and message in the state store.
    /// Set by the worker infrastructure when progress tracking is enabled.
    /// </summary>
    internal Func<int, string?, CancellationToken, Task>? OnReportDetailedProgress { get; init; }

    /// <summary>
    /// Delegate invoked by <see cref="RenewTimeoutAsync"/> to extend the message lock
    /// or visibility timeout by a specific duration. Set by the worker infrastructure.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task>? OnRenewTimeout { get; init; }

    /// <summary>
    /// Delegate invoked by <see cref="CompleteAsync"/> to remove the message from the queue.
    /// Set by the worker infrastructure.
    /// </summary>
    internal Func<CancellationToken, Task>? OnComplete { get; init; }

    /// <summary>
    /// Delegate invoked by <see cref="AbandonAsync(TimeSpan, CancellationToken)"/> to
    /// return the message to the queue for redelivery. Set by the worker infrastructure.
    /// </summary>
    internal Func<TimeSpan, CancellationToken, Task>? OnAbandon { get; init; }

    /// <summary>
    /// Indicates whether the handler explicitly completed the message via <see cref="CompleteAsync"/>.
    /// When true, the worker infrastructure will skip automatic completion.
    /// </summary>
    public bool IsCompleted { get; internal set; }

    /// <summary>
    /// Indicates whether the handler explicitly abandoned the message via <see cref="AbandonAsync(CancellationToken)"/>
    /// or <see cref="AbandonAsync(TimeSpan, CancellationToken)"/>.
    /// When true, the worker infrastructure will skip automatic abandonment.
    /// </summary>
    public bool IsAbandoned { get; internal set; }

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

    /// <summary>
    /// Completes the message, removing it from the queue permanently.
    /// Use this when <c>AutoComplete</c> is disabled and the handler has finished
    /// processing successfully. If <c>AutoComplete</c> is enabled, the worker
    /// infrastructure will skip its own completion when this has been called.
    /// </summary>
    public async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (OnComplete is not null)
            await OnComplete(cancellationToken).ConfigureAwait(false);

        IsCompleted = true;
    }

    /// <summary>
    /// Abandons the message so it becomes immediately visible for redelivery.
    /// Use this when <c>AutoComplete</c> is disabled and the handler cannot
    /// process the message successfully.
    /// </summary>
    public Task AbandonAsync(CancellationToken cancellationToken = default)
        => AbandonAsync(TimeSpan.Zero, cancellationToken);

    /// <summary>
    /// Abandons the message so it becomes visible for redelivery after the specified delay.
    /// Use this when <c>AutoComplete</c> is disabled and the handler wants to retry
    /// the message after a backoff period.
    /// </summary>
    /// <param name="delay">How long before the message becomes visible again. Use <see cref="TimeSpan.Zero"/> for immediate redelivery.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task AbandonAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        if (OnAbandon is not null)
            await OnAbandon(delay, cancellationToken).ConfigureAwait(false);

        IsAbandoned = true;
    }
}
