namespace Foundatio.Mediator.Queues;

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
///             await queueContext.ReportProgressAsync(ct);
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
    /// Delegate invoked by <see cref="ReportProgressAsync"/> to signal that the handler
    /// is still actively working. Set by the consumer infrastructure.
    /// </summary>
    public Func<CancellationToken, Task>? OnReportProgress { get; init; }

    /// <summary>
    /// Delegate invoked by <see cref="RenewTimeoutAsync"/> to extend the message lock
    /// or visibility timeout. Set by the consumer infrastructure.
    /// </summary>
    public Func<TimeSpan, CancellationToken, Task>? OnRenewTimeout { get; init; }

    /// <summary>
    /// Reports that the handler is still actively processing the message.
    /// For transports that support it, this prevents the message from being
    /// redelivered or timed out during long-running operations.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the progress report is acknowledged.</returns>
    public Task ReportProgressAsync(CancellationToken cancellationToken = default)
        => OnReportProgress?.Invoke(cancellationToken) ?? Task.CompletedTask;

    /// <summary>
    /// Extends the message lock or visibility timeout by the specified duration.
    /// Use this for long-running handlers to prevent the message from being
    /// redelivered to another consumer.
    /// </summary>
    /// <param name="extension">The duration to extend the timeout by.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the timeout is renewed.</returns>
    public Task RenewTimeoutAsync(TimeSpan extension, CancellationToken cancellationToken = default)
        => OnRenewTimeout?.Invoke(extension, cancellationToken) ?? Task.CompletedTask;
}
