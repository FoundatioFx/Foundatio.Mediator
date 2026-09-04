using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Background service that processes messages from a single queue.
/// Runs a receive loop that pulls batches from <see cref="IQueueClient"/> into a bounded
/// <see cref="Channel{T}"/>, then dispatches to N concurrent consumer tasks that
/// deserialize and invoke the handler via <see cref="HandlerRegistration.HandleAsync"/>.
/// </summary>
public sealed class QueueWorker : BackgroundService
{
    private readonly IQueueClient _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly QueueWorkerOptions _options;
    private readonly QueueWorkerInfo? _workerInfo;
    private readonly IQueueJobStateStore? _stateStore;
    private readonly DistributedInfrastructureReady? _infraReady;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<QueueWorker> _logger;
    private static readonly TimeSpan s_defaultStateExpiry = TimeSpan.FromHours(24);

    public QueueWorker(
        IQueueClient client,
        IServiceScopeFactory scopeFactory,
        QueueWorkerOptions options,
        DistributedQueueOptions? distributedOptions,
        ILogger<QueueWorker> logger,
        QueueWorkerInfo? workerInfo = null,
        IQueueJobStateStore? stateStore = null,
        DistributedInfrastructureReady? infraReady = null,
        TimeProvider? timeProvider = null)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _options = options;
        _workerInfo = workerInfo;
        _stateStore = stateStore;
        _infraReady = infraReady;
        _jsonOptions = distributedOptions?.JsonSerializerOptions ?? JsonSerializerOptions.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for queues/topics to be created before polling
        if (_infraReady is not null)
        {
            try { await _infraReady.WaitAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }

        _workerInfo?.Stats.SetRunning(true);

        try
        {
            _logger.LogInformation("Queue worker starting for {QueueName} (concurrency={Concurrency}, prefetch={PrefetchCount})",
                _options.QueueName, _options.Concurrency, _options.PrefetchCount);

            // Bounded channel acts as the bridge between receive loop and consumer tasks.
            // Capacity = concurrency + prefetch so the receive loop can stay ahead of consumers.
            var channel = Channel.CreateBounded<QueueMessage>(new BoundedChannelOptions(_options.Concurrency + _options.PrefetchCount)
            {
                SingleWriter = true,
                SingleReader = _options.Concurrency == 1,
                FullMode = BoundedChannelFullMode.Wait
            });

            // Start consumer tasks
            var consumers = new Task[_options.Concurrency];
            for (int i = 0; i < _options.Concurrency; i++)
                consumers[i] = RunConsumerAsync(channel.Reader, stoppingToken);

            // Receive loop
            try
            {
                await RunReceiveLoopAsync(channel.Writer, stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.Complete();

                // Drain any buffered messages that consumers haven't picked up yet
                // and abandon them so they become visible for redelivery on other nodes.
                while (channel.Reader.TryRead(out var orphan))
                {
                    try { await _client.AbandonAsync(orphan).ConfigureAwait(false); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to abandon buffered message {MessageId} during shutdown", orphan.Id); }
                }

                await Task.WhenAll(consumers).ConfigureAwait(false);
            }

            _logger.LogInformation("Queue worker stopped for {QueueName}", _options.QueueName);
        }
        finally
        {
            _workerInfo?.Stats.SetRunning(false);
        }
    }

    private async Task RunReceiveLoopAsync(ChannelWriter<QueueMessage> writer, CancellationToken stoppingToken)
    {
        int consecutiveErrors = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var messages = await _client.ReceiveAsync(_options.QueueName, _options.PrefetchCount, stoppingToken).ConfigureAwait(false);
                consecutiveErrors = 0;

                foreach (var message in messages)
                    await writer.WriteAsync(message, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, consecutiveErrors - 1), 30));
                _logger.LogError(ex, "Error receiving messages from {QueueName}, retrying in {Delay}...", _options.QueueName, delay);
                try
                {
                    await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task RunConsumerAsync(ChannelReader<QueueMessage> reader, CancellationToken stoppingToken)
    {
        await foreach (var message in reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await ProcessMessageAsync(message, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessMessageAsync(QueueMessage message, CancellationToken stoppingToken)
    {
        // Restore trace context from the enqueuing operation
        ActivityContext parentContext = default;
        if (message.Headers.TryGetValue(MessageHeaders.TraceParent, out var traceParent)
            && ActivityContext.TryParse(traceParent, message.Headers.GetValueOrDefault(MessageHeaders.TraceState), out var parsed))
        {
            parentContext = parsed;
        }

        using var activity = MediatorActivitySource.Instance.StartActivity(
            $"Process {_options.QueueName}",
            ActivityKind.Consumer,
            parentContext);

        // Dead-letter check: if the message has exceeded the max attempts, move it to the DLQ
        if (_options.MaxAttempts >= 0 && message.DequeueCount > _options.MaxAttempts)
        {
            _logger.LogWarning(
                "Message {MessageId} on {QueueName} exceeded max attempts ({DequeueCount}/{MaxAttempts}), dead-lettering",
                message.Id, _options.QueueName, message.DequeueCount, _options.MaxAttempts);

            activity?.SetTag("messaging.dead_letter", true);
            activity?.SetTag("messaging.dead_letter.reason", "MaxAttemptsExceeded");

            _workerInfo?.Stats.IncrementDeadLettered();

            if (_stateStore is not null)
                await TryUpdateStateAsync(() => _stateStore.IncrementCounterAsync(_options.QueueName, "dead_lettered", 1, stoppingToken)).ConfigureAwait(false);

            await DeadLetterAsync(message, $"Exceeded max attempts ({_options.MaxAttempts})").ConfigureAwait(false);
            return;
        }

        // Extract job tracking info
        string? jobId = null;
        var trackProgress = _options.TrackProgress && _stateStore is not null;
        if (trackProgress)
            message.Headers.TryGetValue(MessageHeaders.JobId, out jobId);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        // When auto-renew is disabled, enforce the visibility timeout as a hard deadline.
        // When enabled, the background timer keeps extending the lock so we don't cancel.
        if (!_options.AutoRenewTimeout)
            timeoutCts.CancelAfter(_options.VisibilityTimeout);

        // Start cancellation polling if tracking is enabled and we have a job ID
        Task? cancellationPollTask = null;
        if (trackProgress && jobId is not null)
            cancellationPollTask = PollForCancellationAsync(jobId, timeoutCts, stoppingToken);

        // Start auto-renew timer: renews at 2/3 of the visibility timeout to maintain exclusive access
        Task? autoRenewTask = null;
        if (_options.AutoRenewTimeout)
            autoRenewTask = AutoRenewTimeoutAsync(message, timeoutCts.Token);

        var linkedToken = timeoutCts.Token;

        QueueContext? queueContext = null;

        try
        {
            // Update state to Processing
            if (trackProgress && jobId is not null)
                await TryUpdateStateAsync(() => _stateStore!.UpdateJobStatusAsync(jobId, QueueJobStatus.Processing, startedUtc: _timeProvider.GetUtcNow(), attempt: message.DequeueCount, expiry: s_defaultStateExpiry, cancellationToken: stoppingToken)).ConfigureAwait(false);

            // Deserialize body to typed message
            var typedMessage = JsonSerializer.Deserialize(message.Body.Span, _options.MessageType, _jsonOptions);
            if (typedMessage is null)
            {
                _logger.LogWarning("Failed to deserialize message {MessageId} from {QueueName} as {MessageType}",
                    message.Id, _options.QueueName, _options.MessageType.Name);

                if (jobId is not null)
                    await TryUpdateStateAsync(() => _stateStore!.UpdateJobStatusAsync(jobId, QueueJobStatus.Failed, completedUtc: _timeProvider.GetUtcNow(), errorMessage: $"Deserialization returned null for type {_options.MessageType.Name}", expiry: s_defaultStateExpiry, cancellationToken: stoppingToken)).ConfigureAwait(false);
                await DeadLetterAsync(message, $"Deserialization returned null for type {_options.MessageType.Name}").ConfigureAwait(false);
                return;
            }

            // Build QueueContext with delegates wired to IQueueClient
            queueContext = new QueueContext
            {
                QueueName = _options.QueueName,
                MessageType = _options.MessageType,
                DequeueCount = message.DequeueCount,
                MaxAttempts = _options.MaxAttempts,
                EnqueuedAt = message.EnqueuedAt,
                JobId = jobId,
                OnRenewTimeout = (extension, ct) => _client.RenewTimeoutAsync(message, extension, ct),
                OnReportProgress = ct => _client.RenewTimeoutAsync(message, _options.VisibilityTimeout, ct),
                OnReportDetailedProgress = trackProgress && jobId is not null
                    ? (percent, msg, ct) => UpdateJobProgressAsync(jobId, percent, msg, ct)
                    : null,
                OnComplete = ct => _client.CompleteAsync(message, ct),
                OnAbandon = (delay, ct) => _client.AbandonAsync(message, delay, ct)
            };

            using var callContext = CallContext.Rent().Set(queueContext);

            // Create a scope so scoped services (including IMediator) are resolved correctly
            await using var scope = _scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            // Dispatch through the handler pipeline (skipAuthorization: the originating server already enforced authorization)
            // Pass typeof(object) as responseType so UntypedHandleAsync returns the actual result
            // instead of null (which it does when responseType is null for fire-and-forget scenarios).
            var handlerResult = await _options.Registration.HandleAsync(mediator, typedMessage, callContext, linkedToken, typeof(object), skipAuthorization: true).ConfigureAwait(false);

            // Inspect the handler result to drive message lifecycle.
            // If the handler returns an IResult, use its status to determine completion vs retry vs dead-letter.
            // Void/Task handlers return null, which is treated as success.
            if (handlerResult is IResult result && !result.IsSuccess && !queueContext.IsCompleted && !queueContext.IsAbandoned)
            {
                var errorMessage = !string.IsNullOrEmpty(result.Message) ? result.Message : $"Handler returned {result.Status}";

                if (IsRetryableStatus(result.Status))
                {
                    // Retryable failure — abandon for retry
                    _logger.LogWarning("Handler returned retryable status {Status} for message {MessageId} on {QueueName}: {Message}",
                        result.Status, message.Id, _options.QueueName, errorMessage);

                    if (jobId is not null)
                        await TryUpdateStateAsync(() => _stateStore!.UpdateJobStatusAsync(jobId, QueueJobStatus.Failed, completedUtc: _timeProvider.GetUtcNow(), errorMessage: errorMessage, expiry: s_defaultStateExpiry, cancellationToken: stoppingToken)).ConfigureAwait(false);

                    if (_options.AutoComplete)
                        await AbandonAsync(message, stoppingToken).ConfigureAwait(false);

                    _workerInfo?.Stats.IncrementFailed();

                    if (_stateStore is not null)
                        await TryUpdateStateAsync(() => _stateStore.IncrementCounterAsync(_options.QueueName, "failed", 1, stoppingToken)).ConfigureAwait(false);

                    return;
                }
                else
                {
                    // Non-retryable failure — dead-letter immediately (e.g. NotFound, Invalid, Unauthorized)
                    _logger.LogWarning("Handler returned non-retryable status {Status} for message {MessageId} on {QueueName}: {Message}",
                        result.Status, message.Id, _options.QueueName, errorMessage);

                    if (jobId is not null)
                        await TryUpdateStateAsync(() => _stateStore!.UpdateJobStatusAsync(jobId, QueueJobStatus.Failed, completedUtc: _timeProvider.GetUtcNow(), errorMessage: errorMessage, expiry: s_defaultStateExpiry, cancellationToken: stoppingToken)).ConfigureAwait(false);

                    _workerInfo?.Stats.IncrementDeadLettered();

                    if (_stateStore is not null)
                        await TryUpdateStateAsync(() => _stateStore.IncrementCounterAsync(_options.QueueName, "dead_lettered", 1, stoppingToken)).ConfigureAwait(false);

                    await DeadLetterAsync(message, errorMessage).ConfigureAwait(false);
                    return;
                }
            }

            if (_options.AutoComplete && queueContext is { IsCompleted: false, IsAbandoned: false })
                await _client.CompleteAsync(message, stoppingToken).ConfigureAwait(false);

            _workerInfo?.Stats.IncrementProcessed();

            if (_stateStore is not null)
                await TryUpdateStateAsync(() => _stateStore.IncrementCounterAsync(_options.QueueName, "processed", 1, stoppingToken)).ConfigureAwait(false);

            // Update state to Completed
            if (jobId is not null)
                await TryUpdateStateAsync(() => _stateStore!.UpdateJobStatusAsync(jobId, QueueJobStatus.Completed, completedUtc: _timeProvider.GetUtcNow(), progress: 100, expiry: s_defaultStateExpiry, cancellationToken: stoppingToken)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down — abandon so message becomes visible for retry
            _logger.LogDebug("Host stopping, abandoning message {MessageId} on {QueueName}", message.Id, _options.QueueName);
            if (queueContext is not { IsCompleted: true } and not { IsAbandoned: true })
                await AbandonAsync(message).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (trackProgress && jobId is not null && !stoppingToken.IsCancellationRequested)
        {
            // Could be user-requested cancellation or per-message timeout
            var wasCancellationRequested = await _stateStore!.IsCancellationRequestedAsync(jobId, stoppingToken).ConfigureAwait(false);
            if (wasCancellationRequested)
            {
                _logger.LogInformation("Message {MessageId} on {QueueName} was cancelled by user (job {JobId})", message.Id, _options.QueueName, jobId);
                await TryUpdateStateAsync(() => _stateStore!.UpdateJobStatusAsync(jobId, QueueJobStatus.Cancelled, completedUtc: _timeProvider.GetUtcNow(), expiry: s_defaultStateExpiry, cancellationToken: stoppingToken)).ConfigureAwait(false);

                // User cancellation is a normal completion — complete the message so it
                // doesn't get retried or dead-lettered.
                if (_options.AutoComplete && queueContext is { IsCompleted: false, IsAbandoned: false })
                    await _client.CompleteAsync(message, stoppingToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("Message {MessageId} on {QueueName} timed out after {Timeout}", message.Id, _options.QueueName, _options.VisibilityTimeout);
                await TryUpdateStateAsync(() => _stateStore!.UpdateJobStatusAsync(jobId, QueueJobStatus.Failed, completedUtc: _timeProvider.GetUtcNow(), errorMessage: $"Timed out after {_options.VisibilityTimeout}", expiry: s_defaultStateExpiry, cancellationToken: stoppingToken)).ConfigureAwait(false);
                if (_options.AutoComplete && queueContext is { IsCompleted: false, IsAbandoned: false })
                    await AbandonAsync(message, stoppingToken).ConfigureAwait(false);

                _workerInfo?.Stats.IncrementFailed();

                await TryUpdateStateAsync(() => _stateStore!.IncrementCounterAsync(_options.QueueName, "failed", 1, stoppingToken)).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Per-message timeout (no tracking)
            _logger.LogWarning("Message {MessageId} on {QueueName} timed out after {Timeout}", message.Id, _options.QueueName, _options.VisibilityTimeout);
            if (_options.AutoComplete && queueContext is { IsCompleted: false, IsAbandoned: false })
                await AbandonAsync(message, stoppingToken).ConfigureAwait(false);

            _workerInfo?.Stats.IncrementFailed();

            if (_stateStore is not null)
                await TryUpdateStateAsync(() => _stateStore.IncrementCounterAsync(_options.QueueName, "failed", 1, stoppingToken)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId} on {QueueName} (attempt {DequeueCount}/{MaxAttempts})",
                message.Id, _options.QueueName, message.DequeueCount, _options.MaxAttempts);

            if (jobId is not null)
                await TryUpdateStateAsync(() => _stateStore!.UpdateJobStatusAsync(jobId, QueueJobStatus.Failed, completedUtc: _timeProvider.GetUtcNow(), errorMessage: ex.Message, expiry: s_defaultStateExpiry, cancellationToken: stoppingToken)).ConfigureAwait(false);

            if (_options.AutoComplete && queueContext is { IsCompleted: false, IsAbandoned: false })
                await AbandonAsync(message, stoppingToken).ConfigureAwait(false);

            _workerInfo?.Stats.IncrementFailed();

            if (_stateStore is not null)
                await TryUpdateStateAsync(() => _stateStore.IncrementCounterAsync(_options.QueueName, "failed", 1, stoppingToken)).ConfigureAwait(false);
        }
        finally
        {
            // Stop the auto-renew and cancellation polling tasks
            await timeoutCts.CancelAsync().ConfigureAwait(false);

            if (autoRenewTask is not null)
            {
                try { await autoRenewTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }

            if (cancellationPollTask is not null)
            {
                try { await cancellationPollTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
    }

    private async Task PollForCancellationAsync(string jobId, CancellationTokenSource messageTimeoutCts, CancellationToken stoppingToken)
    {
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(messageTimeoutCts.Token, stoppingToken);
            while (!linkedCts.Token.IsCancellationRequested)
            {
                await Task.Delay(_options.CancellationPollInterval, _timeProvider, linkedCts.Token).ConfigureAwait(false);

                if (await _stateStore!.IsCancellationRequestedAsync(jobId, linkedCts.Token).ConfigureAwait(false))
                {
                    _logger.LogDebug("Cancellation requested for job {JobId} on {QueueName}", jobId, _options.QueueName);
                    await messageTimeoutCts.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when message completes or host stops
        }
    }

    private async Task AutoRenewTimeoutAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        // Renew at 2/3 of the visibility timeout to maintain exclusive access with margin
        var renewInterval = _options.VisibilityTimeout * (2.0 / 3.0);
        if (renewInterval <= TimeSpan.Zero)
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(renewInterval, _timeProvider, cancellationToken).ConfigureAwait(false);

                _logger.LogDebug("Auto-renewing timeout for message {MessageId} on {QueueName} by {Timeout}",
                    message.Id, _options.QueueName, _options.VisibilityTimeout);

                await _client.RenewTimeoutAsync(message, _options.VisibilityTimeout, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when message processing completes or host stops
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-renew timeout for message {MessageId} on {QueueName}; " +
                "message may become visible to other consumers if processing takes longer than {Timeout}",
                message.Id, _options.QueueName, _options.VisibilityTimeout);
        }
    }

    private async Task UpdateJobProgressAsync(string jobId, int percent, string? message, CancellationToken ct)
    {
        if (_stateStore is null) return;

        // Check for cancellation on every progress report — this is intentionally NOT wrapped
        // in TryUpdateStateAsync because cancellation failures must propagate.
        if (await _stateStore.IsCancellationRequestedAsync(jobId, ct).ConfigureAwait(false))
            throw new OperationCanceledException("Job cancellation was requested.");

        await TryUpdateStateAsync(() => _stateStore.UpdateJobProgressAsync(jobId, Math.Clamp(percent, 0, 100), message, s_defaultStateExpiry, ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a state store operation, catching and logging failures so they don't
    /// prevent message processing. State store unavailability is transient and should
    /// not cause messages to be abandoned or dead-lettered.
    /// </summary>
    private async Task TryUpdateStateAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Always propagate cancellation
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update job state store for queue {QueueName}; message processing will continue", _options.QueueName);
        }
    }

    private async Task AbandonAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        var delay = QueueRetryDelay.Compute(_options.RetryPolicy, _options.RetryDelay, message.DequeueCount);
        if (delay > TimeSpan.Zero)
        {
            try
            {
                await _client.AbandonAsync(message, delay, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to abandon message {MessageId} on {QueueName} with delay {Delay}",
                    message.Id, _options.QueueName, delay);
            }
        }

        await AbandonAsync(message).ConfigureAwait(false);
    }

    private async Task AbandonAsync(QueueMessage message)
    {
        try
        {
            await _client.AbandonAsync(message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to abandon message {MessageId} on {QueueName}", message.Id, _options.QueueName);
        }
    }

    private async Task DeadLetterAsync(QueueMessage message, string reason)
    {
        try
        {
            await _client.DeadLetterAsync(message, reason).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dead-letter message {MessageId} on {QueueName}: {Reason}",
                message.Id, _options.QueueName, reason);
        }
    }

    /// <summary>
    /// Determines whether a failed <see cref="ResultStatus"/> represents a transient condition
    /// that may succeed on retry. Non-retryable statuses are routed to the dead-letter queue
    /// immediately because the problem is with the message content, not infrastructure.
    /// </summary>
    private static bool IsRetryableStatus(ResultStatus status) => status switch
    {
        // Transient / infrastructure errors — worth retrying
        ResultStatus.Error => true,
        ResultStatus.Unavailable => true,

        // Permanent / content errors — retrying won't help
        ResultStatus.CriticalError => false,
        ResultStatus.NotFound => false,
        ResultStatus.Invalid => false,
        ResultStatus.BadRequest => false,
        ResultStatus.Unauthorized => false,
        ResultStatus.Forbidden => false,
        ResultStatus.Conflict => false,

        // Success statuses should never reach here, but treat them as non-retryable
        _ => false
    };
}
