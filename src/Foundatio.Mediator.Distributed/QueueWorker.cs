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
        TimeProvider? timeProvider = null)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _options = options;
        _workerInfo = workerInfo;
        _stateStore = stateStore;
        _jsonOptions = distributedOptions?.JsonSerializerOptions ?? JsonSerializerOptions.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_workerInfo is not null)
            _workerInfo._isRunning = true;

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
                await Task.WhenAll(consumers).ConfigureAwait(false);
            }

            _logger.LogInformation("Queue worker stopped for {QueueName}", _options.QueueName);
        }
        finally
        {
            if (_workerInfo is not null)
                _workerInfo._isRunning = false;
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

        // Dead-letter check: if the message has exceeded the retry limit, move it to the DLQ
        if (_options.MaxRetries >= 0 && message.DequeueCount > _options.MaxRetries + 1)
        {
            _logger.LogWarning(
                "Message {MessageId} on {QueueName} exceeded max retries ({DequeueCount}/{MaxRetries}), dead-lettering",
                message.Id, _options.QueueName, message.DequeueCount, _options.MaxRetries);

            activity?.SetTag("messaging.dead_letter", true);
            activity?.SetTag("messaging.dead_letter.reason", "MaxRetriesExceeded");

            if (_workerInfo is not null)
                Interlocked.Increment(ref _workerInfo._messagesDeadLettered);

            if (_stateStore is not null)
                await _stateStore.IncrementCounterAsync(_options.QueueName, "dead_lettered", 1, stoppingToken).ConfigureAwait(false);

            await DeadLetterAsync(message, $"Exceeded max retries ({_options.MaxRetries})").ConfigureAwait(false);
            return;
        }

        // Extract job tracking info
        string? jobId = null;
        var trackProgress = _options.TrackProgress && _stateStore is not null;
        if (trackProgress)
            message.Headers.TryGetValue(MessageHeaders.JobId, out jobId);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(_options.VisibilityTimeout);

        // Start cancellation polling if tracking is enabled and we have a job ID
        Task? cancellationPollTask = null;
        if (trackProgress && jobId is not null)
            cancellationPollTask = PollForCancellationAsync(jobId, timeoutCts, stoppingToken);

        var linkedToken = timeoutCts.Token;

        try
        {
            // Update state to Processing
            if (trackProgress && jobId is not null)
            {
                var now = _timeProvider.GetUtcNow();
                var state = await _stateStore!.GetJobStateAsync(jobId, stoppingToken).ConfigureAwait(false);
                if (state is not null)
                {
                    state.Status = QueueJobStatus.Processing;
                    state.StartedUtc = now;
                    state.LastUpdatedUtc = now;
                    await _stateStore.SetJobStateAsync(state, s_defaultStateExpiry, stoppingToken).ConfigureAwait(false);
                }
            }

            // Deserialize body to typed message
            var typedMessage = JsonSerializer.Deserialize(message.Body.Span, _options.MessageType, _jsonOptions);
            if (typedMessage is null)
            {
                _logger.LogWarning("Failed to deserialize message {MessageId} from {QueueName} as {MessageType}",
                    message.Id, _options.QueueName, _options.MessageType.Name);

                await UpdateJobStateFailed(jobId, $"Deserialization returned null for type {_options.MessageType.Name}", stoppingToken).ConfigureAwait(false);
                await DeadLetterAsync(message, $"Deserialization returned null for type {_options.MessageType.Name}").ConfigureAwait(false);
                return;
            }

            // Build QueueContext with delegates wired to IQueueClient
            var queueContext = new QueueContext
            {
                QueueName = _options.QueueName,
                MessageType = _options.MessageType,
                DequeueCount = message.DequeueCount,
                MaxRetries = _options.MaxRetries,
                EnqueuedAt = message.EnqueuedAt,
                JobId = jobId,
                OnRenewTimeout = (extension, ct) => _client.RenewTimeoutAsync(message, extension, ct),
                OnReportProgress = ct => _client.RenewTimeoutAsync(message, _options.VisibilityTimeout, ct),
                OnReportDetailedProgress = trackProgress && jobId is not null
                    ? (percent, msg, ct) => UpdateJobProgressAsync(jobId, percent, msg, ct)
                    : null
            };

            using var callContext = CallContext.Rent().Set(queueContext);

            // Create a scope so scoped services (including IMediator) are resolved correctly
            await using var scope = _scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            // Dispatch through the handler pipeline (skipAuthorization: the originating server already enforced authorization)
            await _options.Registration.HandleAsync(mediator, typedMessage, callContext, linkedToken, null, skipAuthorization: true).ConfigureAwait(false);

            if (_options.AutoComplete)
                await _client.CompleteAsync(message, stoppingToken).ConfigureAwait(false);

            if (_workerInfo is not null)
                Interlocked.Increment(ref _workerInfo._messagesProcessed);

            if (_stateStore is not null)
                await _stateStore.IncrementCounterAsync(_options.QueueName, "processed", 1, stoppingToken).ConfigureAwait(false);

            // Update state to Completed
            await UpdateJobStateCompleted(jobId, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down — abandon so message becomes visible for retry
            _logger.LogDebug("Host stopping, abandoning message {MessageId} on {QueueName}", message.Id, _options.QueueName);
            await AbandonAsync(message).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (trackProgress && jobId is not null && !stoppingToken.IsCancellationRequested)
        {
            // Could be user-requested cancellation or per-message timeout
            var wasCancellationRequested = await _stateStore!.IsCancellationRequestedAsync(jobId, stoppingToken).ConfigureAwait(false);
            if (wasCancellationRequested)
            {
                _logger.LogInformation("Message {MessageId} on {QueueName} was cancelled (job {JobId})", message.Id, _options.QueueName, jobId);
                await UpdateJobStateCancelled(jobId, stoppingToken).ConfigureAwait(false);
                if (_options.AutoComplete)
                    await _client.CompleteAsync(message, stoppingToken).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("Message {MessageId} on {QueueName} timed out after {Timeout}", message.Id, _options.QueueName, _options.VisibilityTimeout);
                await UpdateJobStateFailed(jobId, $"Timed out after {_options.VisibilityTimeout}", stoppingToken).ConfigureAwait(false);
                if (_options.AutoComplete)
                    await AbandonAsync(message, stoppingToken).ConfigureAwait(false);
            }

            if (_workerInfo is not null)
                Interlocked.Increment(ref _workerInfo._messagesFailed);

            if (_stateStore is not null)
                await _stateStore.IncrementCounterAsync(_options.QueueName, "failed", 1, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Per-message timeout (no tracking)
            _logger.LogWarning("Message {MessageId} on {QueueName} timed out after {Timeout}", message.Id, _options.QueueName, _options.VisibilityTimeout);
            if (_options.AutoComplete)
                await AbandonAsync(message, stoppingToken).ConfigureAwait(false);

            if (_workerInfo is not null)
                Interlocked.Increment(ref _workerInfo._messagesFailed);

            if (_stateStore is not null)
                await _stateStore.IncrementCounterAsync(_options.QueueName, "failed", 1, stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message {MessageId} on {QueueName} (attempt {DequeueCount}/{MaxAttempts})",
                message.Id, _options.QueueName, message.DequeueCount, _options.MaxRetries + 1);

            await UpdateJobStateFailed(jobId, ex.Message, stoppingToken).ConfigureAwait(false);

            if (_options.AutoComplete)
                await AbandonAsync(message, stoppingToken).ConfigureAwait(false);

            if (_workerInfo is not null)
                Interlocked.Increment(ref _workerInfo._messagesFailed);

            if (_stateStore is not null)
                await _stateStore.IncrementCounterAsync(_options.QueueName, "failed", 1, stoppingToken).ConfigureAwait(false);
        }
        finally
        {
            // Stop the cancellation polling task
            if (cancellationPollTask is not null)
            {
                timeoutCts.Cancel();
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

    private async Task UpdateJobProgressAsync(string jobId, int percent, string? message, CancellationToken ct)
    {
        if (_stateStore is null) return;

        // Check for cancellation on every progress report
        if (await _stateStore.IsCancellationRequestedAsync(jobId, ct).ConfigureAwait(false))
            throw new OperationCanceledException("Job cancellation was requested.");

        var state = await _stateStore.GetJobStateAsync(jobId, ct).ConfigureAwait(false);
        if (state is null) return;

        state.Progress = Math.Clamp(percent, 0, 100);
        state.ProgressMessage = message;
        state.LastUpdatedUtc = _timeProvider.GetUtcNow();
        await _stateStore.SetJobStateAsync(state, s_defaultStateExpiry, ct).ConfigureAwait(false);
    }

    private async Task UpdateJobStateCompleted(string? jobId, CancellationToken ct)
    {
        if (jobId is null || _stateStore is null) return;

        var state = await _stateStore.GetJobStateAsync(jobId, ct).ConfigureAwait(false);
        if (state is null) return;

        var now = _timeProvider.GetUtcNow();
        state.Status = QueueJobStatus.Completed;
        state.Progress = 100;
        state.CompletedUtc = now;
        state.LastUpdatedUtc = now;
        await _stateStore.SetJobStateAsync(state, s_defaultStateExpiry, ct).ConfigureAwait(false);
    }

    private async Task UpdateJobStateFailed(string? jobId, string errorMessage, CancellationToken ct)
    {
        if (jobId is null || _stateStore is null) return;

        var state = await _stateStore.GetJobStateAsync(jobId, ct).ConfigureAwait(false);
        if (state is null) return;

        var now = _timeProvider.GetUtcNow();
        state.Status = QueueJobStatus.Failed;
        state.ErrorMessage = errorMessage;
        state.CompletedUtc = now;
        state.LastUpdatedUtc = now;
        await _stateStore.SetJobStateAsync(state, s_defaultStateExpiry, ct).ConfigureAwait(false);
    }

    private async Task UpdateJobStateCancelled(string? jobId, CancellationToken ct)
    {
        if (jobId is null || _stateStore is null) return;

        var state = await _stateStore.GetJobStateAsync(jobId, ct).ConfigureAwait(false);
        if (state is null) return;

        var now = _timeProvider.GetUtcNow();
        state.Status = QueueJobStatus.Cancelled;
        state.CompletedUtc = now;
        state.LastUpdatedUtc = now;
        await _stateStore.SetJobStateAsync(state, s_defaultStateExpiry, ct).ConfigureAwait(false);
    }

    private async Task AbandonAsync(QueueMessage message, CancellationToken cancellationToken)
    {
        var delay = ComputeRetryDelay(message.DequeueCount);
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

    public TimeSpan ComputeRetryDelay(int dequeueCount)
    {
        if (_options.RetryPolicy == QueueRetryPolicy.None)
            return TimeSpan.Zero;

        var baseDelay = _options.RetryDelay;
        if (baseDelay <= TimeSpan.Zero)
            return TimeSpan.Zero;

        // dequeueCount is 1-based; first retry is after attempt 1
        int retryNumber = Math.Max(0, dequeueCount - 1);

        double delayMs = _options.RetryPolicy switch
        {
            QueueRetryPolicy.Fixed => baseDelay.TotalMilliseconds,
            QueueRetryPolicy.Exponential => baseDelay.TotalMilliseconds * Math.Pow(2, retryNumber),
            _ => 0
        };

        // Apply proportional jitter (±10% of the computed delay)
        double jitterRange = delayMs * 0.1;
        double jitter = (Random.Shared.NextDouble() * 2 - 1) * jitterRange;
        delayMs = Math.Max(0, delayMs + jitter);

        // Cap at 15 minutes to prevent unreasonably long delays
        return TimeSpan.FromMilliseconds(Math.Min(delayMs, TimeSpan.FromMinutes(15).TotalMilliseconds));
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
}
