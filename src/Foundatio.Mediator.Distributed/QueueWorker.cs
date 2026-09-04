using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Background service that processes messages from a single queue. A receive loop pulls batches
/// from the <see cref="IQueueClient"/> into a bounded channel; N consumer tasks deserialize each
/// message by its <see cref="MessageHeaders.MessageType"/> header and dispatch it to every handler
/// on the queue that accepts that type, through the normal middleware pipeline.
/// </summary>
/// <remarks>
/// On host stop the receive loop ends immediately, buffered messages are abandoned so another
/// worker picks them up, and in-flight handlers get <see cref="DistributedQueueOptions.ShutdownTimeout"/>
/// to finish before they are cancelled and their messages abandoned. Acknowledgements of finished
/// work never observe the stopping token, so a completed handler is never re-run because of a deploy.
/// </remarks>
public sealed class QueueWorker : BackgroundService
{
    private static readonly TimeSpan s_ackTimeout = TimeSpan.FromSeconds(30);

    private readonly IQueueClient _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly QueueWorkerOptions _options;
    private readonly QueueWorkerInfo? _workerInfo;
    private readonly IQueueJobStateStore? _stateStore;
    private readonly DistributedInfrastructureReady? _infraReady;
    private readonly MessageTypeResolver? _typeResolver;
    private readonly IQueueHeaderProvider[] _headerProviders;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<QueueWorker> _logger;
    private readonly TimeSpan _shutdownTimeout;
    private readonly TimeSpan _stateExpiry;

    public QueueWorker(
        IQueueClient client,
        IServiceScopeFactory scopeFactory,
        QueueWorkerOptions options,
        DistributedQueueOptions? distributedOptions,
        ILogger<QueueWorker> logger,
        QueueWorkerInfo? workerInfo = null,
        IQueueJobStateStore? stateStore = null,
        DistributedInfrastructureReady? infraReady = null,
        TimeProvider? timeProvider = null,
        MessageTypeResolver? typeResolver = null,
        IEnumerable<IQueueHeaderProvider>? headerProviders = null)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _options = options;
        _workerInfo = workerInfo;
        _stateStore = stateStore;
        _infraReady = infraReady;
        _typeResolver = typeResolver;
        _headerProviders = headerProviders?.ToArray() ?? [];
        _jsonOptions = distributedOptions?.JsonSerializerOptions ?? JsonSerializerOptions.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
        _shutdownTimeout = distributedOptions?.ShutdownTimeout ?? TimeSpan.FromSeconds(30);
        _stateExpiry = distributedOptions?.JobStateExpiry ?? TimeSpan.FromHours(24);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_infraReady is not null)
        {
            try { await _infraReady.WaitAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }

        _workerInfo?.Stats.SetRunning(true);

        // Handlers keep running for the drain window after the host starts stopping.
        var drainCts = new CancellationTokenSource();
        var stopRegistration = stoppingToken.Register(() =>
        {
            if (_shutdownTimeout <= TimeSpan.Zero)
                drainCts.Cancel();
            else
                drainCts.CancelAfter(_shutdownTimeout);
        });

        try
        {
            _logger.LogInformation("Queue worker starting for {QueueName} (concurrency={Concurrency}, prefetch={PrefetchCount}, handlers={HandlerCount})",
                _options.QueueName, _options.Concurrency, _options.PrefetchCount, _options.Registrations.Count);

            var channel = Channel.CreateBounded<QueueMessage>(new BoundedChannelOptions(_options.Concurrency + _options.PrefetchCount)
            {
                SingleWriter = true,
                SingleReader = _options.Concurrency == 1,
                FullMode = BoundedChannelFullMode.Wait
            });

            var consumers = new Task[_options.Concurrency];
            for (int i = 0; i < _options.Concurrency; i++)
                consumers[i] = RunConsumerAsync(channel.Reader, drainCts.Token);

            try
            {
                await RunReceiveLoopAsync(channel.Writer, stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                channel.Writer.Complete();

                // Buffered messages are already invisible at the transport; hand them back so another worker gets them now.
                while (channel.Reader.TryRead(out var orphan))
                    await AbandonForRedeliveryAsync(orphan, "shutdown").ConfigureAwait(false);

                await Task.WhenAll(consumers).ConfigureAwait(false);
            }

            _logger.LogInformation("Queue worker stopped for {QueueName}", _options.QueueName);
        }
        finally
        {
            _workerInfo?.Stats.SetRunning(false);
            stopRegistration.Dispose();
            drainCts.Dispose();
        }
    }

    private async Task RunReceiveLoopAsync(ChannelWriter<QueueMessage> writer, CancellationToken stoppingToken)
    {
        int consecutiveErrors = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            IReadOnlyList<QueueMessage> messages;
            try
            {
                messages = await _client.ReceiveAsync(_options.QueueName, _options.PrefetchCount, _options.VisibilityTimeout, stoppingToken).ConfigureAwait(false);
                consecutiveErrors = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveErrors++;
                var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, consecutiveErrors - 1), 30));
                _logger.LogError(ex, "Error receiving messages from {QueueName}, retrying in {Delay}", _options.QueueName, delay);
                try { await Task.Delay(delay, _timeProvider, stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                continue;
            }

            for (int i = 0; i < messages.Count; i++)
            {
                try
                {
                    await writer.WriteAsync(messages[i], stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The rest of this batch never reached a consumer; release it immediately.
                    for (int j = i; j < messages.Count; j++)
                        await AbandonForRedeliveryAsync(messages[j], "shutdown").ConfigureAwait(false);
                    return;
                }
            }
        }
    }

    private async Task RunConsumerAsync(ChannelReader<QueueMessage> reader, CancellationToken drainToken)
    {
        await foreach (var message in reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                await ProcessMessageAsync(message, drainToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // ProcessMessageAsync handles its own failures; anything escaping is a bug in the worker itself.
                _logger.LogError(ex, "Unexpected error processing message {MessageId} on {QueueName}; the consumer will continue with the next message",
                    message.Id, _options.QueueName);
                await AbandonForRedeliveryAsync(message, "worker-error").ConfigureAwait(false);
            }
        }
    }

    private async Task ProcessMessageAsync(QueueMessage message, CancellationToken drainToken)
    {
        ActivityContext producerContext = default;
        if (message.Headers.TryGetValue(MessageHeaders.TraceParent, out var traceParent)
            && ActivityContext.TryParse(traceParent, message.Headers.GetValueOrDefault(MessageHeaders.TraceState), out var parsed))
        {
            producerContext = parsed;
        }

        // Link to the producer rather than parenting under it, so an hours-long job does not stretch the caller's trace.
        using var activity = MediatorActivitySource.Instance.StartActivity(
            $"Process {_options.QueueName}",
            ActivityKind.Consumer,
            parentContext: default,
            links: producerContext != default ? [new ActivityLink(producerContext)] : null);
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", _options.QueueName);
        activity?.SetTag("messaging.message.id", message.Id);
        activity?.SetTag("messaging.message.dequeue_count", message.DequeueCount);
        if (message.Headers.TryGetValue(MessageHeaders.CorrelationId, out var correlationId))
            activity?.SetTag("messaging.message.conversation_id", correlationId);

        var tags = DistributedMetrics.Tags(_options.QueueName, _options.MessageType.Name, _options.Group);

        if (_options.MaxAttempts >= 0 && message.DequeueCount > _options.MaxAttempts)
        {
            _logger.LogWarning(
                "Message {MessageId} on {QueueName} exceeded max attempts ({DequeueCount}/{MaxAttempts}), dead-lettering",
                message.Id, _options.QueueName, message.DequeueCount, _options.MaxAttempts);

            activity?.SetTag("messaging.dead_letter.reason", "MaxAttemptsExceeded");
            await RecordDeadLetterAsync(message, $"Exceeded max attempts ({_options.MaxAttempts})", jobId: null, tags).ConfigureAwait(false);
            return;
        }

        string? jobId = null;
        var trackProgress = _options.TrackProgress && _stateStore is not null;
        if (trackProgress)
            message.Headers.TryGetValue(MessageHeaders.JobId, out jobId);
        if (jobId is not null)
            activity?.SetTag("messaging.job.id", jobId);

        var messageType = ResolveMessageType(message);
        if (messageType is null)
        {
            var typeName = message.Headers.GetValueOrDefault(MessageHeaders.MessageType) ?? "<none>";
            _logger.LogWarning("Message {MessageId} on {QueueName} has unknown type '{TypeName}', dead-lettering", message.Id, _options.QueueName, typeName);
            await FailJobAsync(jobId, $"Unknown message type '{typeName}'").ConfigureAwait(false);
            await RecordDeadLetterAsync(message, $"Unknown message type '{typeName}'", jobId, tags).ConfigureAwait(false);
            return;
        }
        activity?.SetTag("messaging.message.type", messageType.FullName);

        object? typedMessage;
        try
        {
            typedMessage = JsonSerializer.Deserialize(message.Body.Span, messageType, _jsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Message {MessageId} on {QueueName} could not be deserialized as {MessageType}, dead-lettering", message.Id, _options.QueueName, messageType.Name);
            await FailJobAsync(jobId, $"Deserialization failed for type {messageType.Name}: {ex.Message}").ConfigureAwait(false);
            await RecordDeadLetterAsync(message, $"Deserialization failed for type {messageType.Name}: {ex.Message}", jobId, tags).ConfigureAwait(false);
            return;
        }

        if (typedMessage is null)
        {
            _logger.LogWarning("Message {MessageId} on {QueueName} deserialized to null as {MessageType}, dead-lettering", message.Id, _options.QueueName, messageType.Name);
            await FailJobAsync(jobId, $"Deserialization returned null for type {messageType.Name}").ConfigureAwait(false);
            await RecordDeadLetterAsync(message, $"Deserialization returned null for type {messageType.Name}", jobId, tags).ConfigureAwait(false);
            return;
        }

        var handlers = HandlersFor(messageType);
        if (handlers.Count == 0)
        {
            _logger.LogWarning("No handler on {QueueName} accepts message type {MessageType}, dead-lettering {MessageId}", _options.QueueName, messageType.Name, message.Id);
            await FailJobAsync(jobId, $"No handler accepts message type {messageType.Name}").ConfigureAwait(false);
            await RecordDeadLetterAsync(message, $"No handler accepts message type {messageType.Name}", jobId, tags).ConfigureAwait(false);
            return;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(drainToken);
        if (!_options.AutoRenewTimeout)
            timeoutCts.CancelAfter(_options.VisibilityTimeout);

        Task? cancellationPollTask = null;
        if (jobId is not null)
            cancellationPollTask = PollForCancellationAsync(jobId, timeoutCts, drainToken);

        Task? autoRenewTask = null;
        if (_options.AutoRenewTimeout)
            autoRenewTask = AutoRenewTimeoutAsync(message, jobId, timeoutCts.Token);

        var handlerToken = timeoutCts.Token;
        var stopwatch = Stopwatch.StartNew();
        DistributedMetrics.InFlight.Add(1, tags);
        QueueContext? queueContext = null;

        try
        {
            if (jobId is not null)
                await TryUpdateStateAsync(() => _stateStore!.UpdateJobStatusAsync(jobId, QueueJobStatus.Processing, startedUtc: _timeProvider.GetUtcNow(), attempt: message.DequeueCount, expiry: _stateExpiry, cancellationToken: AckToken())).ConfigureAwait(false);

            queueContext = new QueueContext
            {
                QueueName = _options.QueueName,
                MessageId = message.Id,
                VisibilityTimeout = _options.VisibilityTimeout,
                MessageType = messageType,
                DequeueCount = message.DequeueCount,
                MaxAttempts = _options.MaxAttempts,
                EnqueuedAt = message.EnqueuedAt,
                JobId = jobId,
                OnRenewTimeout = (extension, ct) => _client.RenewTimeoutAsync(message, extension, ct),
                OnReportProgress = ct => HeartbeatAsync(message, jobId, ct),
                OnReportDetailedProgress = jobId is not null
                    ? (percent, msg, ct) => UpdateJobProgressAsync(jobId, percent, msg, ct)
                    : null,
                OnComplete = ct => _client.CompleteAsync(message, ct),
                OnAbandon = (delay, ct) => _client.AbandonAsync(message, delay, ct)
            };

            using var callContext = CallContext.Rent().Set(queueContext);
            RestoreHeaders(message, callContext);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            IResult? failure = null;
            foreach (var handler in handlers)
            {
                // Authorization ran on the enqueuing node; typeof(object) makes the wrapper return the real result.
                var handlerResult = await handler.HandleAsync(mediator, typedMessage, callContext, handlerToken, typeof(object), skipAuthorization: true).ConfigureAwait(false);

                if (handlerResult is IResult { IsSuccess: false } result)
                {
                    failure = result;
                    break;
                }
            }

            stopwatch.Stop();

            if (failure is not null && !queueContext.IsCompleted && !queueContext.IsAbandoned)
            {
                var errorMessage = !string.IsNullOrEmpty(failure.Message) ? failure.Message : $"Handler returned {failure.Status}";
                activity?.SetStatus(ActivityStatusCode.Error, errorMessage);

                if (IsRetryableStatus(failure.Status))
                {
                    _logger.LogWarning("Handler returned retryable status {Status} for message {MessageId} on {QueueName}: {Message}",
                        failure.Status, message.Id, _options.QueueName, errorMessage);

                    await FailJobAsync(jobId, errorMessage).ConfigureAwait(false);
                    await RecordFailureAsync(message, tags, stopwatch.Elapsed).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogWarning("Handler returned non-retryable status {Status} for message {MessageId} on {QueueName}: {Message}",
                        failure.Status, message.Id, _options.QueueName, errorMessage);

                    await FailJobAsync(jobId, errorMessage).ConfigureAwait(false);
                    await RecordDeadLetterAsync(message, $"{failure.Status}: {errorMessage}", jobId, tags).ConfigureAwait(false);
                }

                return;
            }

            if (_options.AutoComplete && queueContext is { IsCompleted: false, IsAbandoned: false })
                await _client.CompleteAsync(message, AckToken()).ConfigureAwait(false);

            _workerInfo?.Stats.IncrementProcessed();
            DistributedMetrics.Processed.Add(1, tags);
            DistributedMetrics.HandlerDuration.Record(stopwatch.Elapsed.TotalMilliseconds, DistributedMetrics.Tags(_options.QueueName, _options.MessageType.Name, _options.Group, "processed"));

            if (_stateStore is not null)
                await TryUpdateStateAsync(() => _stateStore.IncrementCounterAsync(_options.QueueName, "processed", 1, AckToken())).ConfigureAwait(false);

            if (jobId is not null)
                await TryUpdateStateAsync(() => _stateStore!.UpdateJobStatusAsync(jobId, QueueJobStatus.Completed, completedUtc: _timeProvider.GetUtcNow(), progress: 100, expiry: _stateExpiry, cancellationToken: AckToken())).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (drainToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogInformation("Host stopping, abandoning in-flight message {MessageId} on {QueueName} for redelivery", message.Id, _options.QueueName);
            activity?.SetStatus(ActivityStatusCode.Error, "shutdown");
            if (queueContext is not { IsCompleted: true } and not { IsAbandoned: true })
                await AbandonForRedeliveryAsync(message, "shutdown").ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            bool cancelledByUser = false;
            if (jobId is not null)
            {
                try { cancelledByUser = await _stateStore!.IsCancellationRequestedAsync(jobId, AckToken()).ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to read cancellation state for job {JobId}", jobId); }
            }

            if (cancelledByUser)
            {
                _logger.LogInformation("Message {MessageId} on {QueueName} was cancelled by request (job {JobId})", message.Id, _options.QueueName, jobId);
                activity?.SetTag("messaging.job.cancelled", true);
                await TryUpdateStateAsync(() => _stateStore!.UpdateJobStatusAsync(jobId!, QueueJobStatus.Cancelled, completedUtc: _timeProvider.GetUtcNow(), expiry: _stateExpiry, cancellationToken: AckToken())).ConfigureAwait(false);

                // A requested cancellation is a normal completion; the message must not be retried.
                if (_options.AutoComplete && queueContext is { IsCompleted: false, IsAbandoned: false })
                    await TryAckAsync(() => _client.CompleteAsync(message, AckToken()), message, "complete").ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("Message {MessageId} on {QueueName} timed out after {Timeout}", message.Id, _options.QueueName, _options.VisibilityTimeout);
                activity?.SetStatus(ActivityStatusCode.Error, "timeout");
                await FailJobAsync(jobId, $"Timed out after {_options.VisibilityTimeout}").ConfigureAwait(false);
                if (queueContext is not { IsCompleted: true } and not { IsAbandoned: true })
                    await RecordFailureAsync(message, tags, stopwatch.Elapsed).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing message {MessageId} on {QueueName} (attempt {DequeueCount}/{MaxAttempts})",
                message.Id, _options.QueueName, message.DequeueCount, _options.MaxAttempts);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            await FailJobAsync(jobId, ex.Message).ConfigureAwait(false);
            if (queueContext is not { IsCompleted: true } and not { IsAbandoned: true })
                await RecordFailureAsync(message, tags, stopwatch.Elapsed).ConfigureAwait(false);
        }
        finally
        {
            DistributedMetrics.InFlight.Add(-1, tags);
            await timeoutCts.CancelAsync().ConfigureAwait(false);

            if (autoRenewTask is not null)
                await autoRenewTask.ConfigureAwait(false);

            if (cancellationPollTask is not null)
                await cancellationPollTask.ConfigureAwait(false);
        }
    }

    private Type? ResolveMessageType(QueueMessage message)
    {
        if (!message.Headers.TryGetValue(MessageHeaders.MessageType, out var typeName) || string.IsNullOrEmpty(typeName))
            return _options.MessageType;

        if (string.Equals(typeName, _options.MessageType.FullName, StringComparison.Ordinal) || string.Equals(typeName, _options.MessageType.AssemblyQualifiedName, StringComparison.Ordinal))
            return _options.MessageType;

        if (_typeResolver is not null)
        {
            foreach (var handler in _options.Registrations)
            {
                if (handler.MessageType is { } declared && _typeResolver.TryResolve(typeName, declared) is { } resolved)
                    return resolved;
            }
        }

        return null;
    }

    private List<HandlerRegistration> HandlersFor(Type messageType)
    {
        var handlers = new List<HandlerRegistration>(_options.Registrations.Count);
        foreach (var handler in _options.Registrations)
        {
            if (handler.MessageType is { } declared && declared.IsAssignableFrom(messageType))
                handlers.Add(handler);
        }

        return handlers;
    }

    private void RestoreHeaders(QueueMessage message, CallContext callContext)
    {
        foreach (var provider in _headerProviders)
        {
            try
            {
                provider.Restore(message.Headers, callContext);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Header provider {Provider} failed to restore context for message {MessageId} on {QueueName}",
                    provider.GetType().Name, message.Id, _options.QueueName);
            }
        }
    }

    private async Task PollForCancellationAsync(string jobId, CancellationTokenSource messageTimeoutCts, CancellationToken drainToken)
    {
        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(messageTimeoutCts.Token, drainToken);
            while (!linkedCts.Token.IsCancellationRequested)
            {
                await Task.Delay(_options.CancellationPollInterval, _timeProvider, linkedCts.Token).ConfigureAwait(false);

                bool requested;
                try
                {
                    requested = await _stateStore!.IsCancellationRequestedAsync(jobId, linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linkedCts.Token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to poll cancellation for job {JobId} on {QueueName}; will retry", jobId, _options.QueueName);
                    continue;
                }

                if (requested)
                {
                    _logger.LogDebug("Cancellation requested for job {JobId} on {QueueName}", jobId, _options.QueueName);
                    await messageTimeoutCts.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Message finished or host is stopping
        }
    }

    private async Task AutoRenewTimeoutAsync(QueueMessage message, string? jobId, CancellationToken cancellationToken)
    {
        // Renew at 2/3 of the visibility timeout to keep the lock with margin
        var renewInterval = _options.VisibilityTimeout * (2.0 / 3.0);
        if (renewInterval <= TimeSpan.Zero)
            return;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(renewInterval, _timeProvider, cancellationToken).ConfigureAwait(false);

                _logger.LogDebug("Auto-renewing timeout for message {MessageId} on {QueueName} by {Timeout}",
                    message.Id, _options.QueueName, _options.VisibilityTimeout);

                await _client.RenewTimeoutAsync(message, _options.VisibilityTimeout, cancellationToken).ConfigureAwait(false);

                if (jobId is not null && _stateStore is not null)
                    await TryUpdateStateAsync(() => _stateStore.HeartbeatAsync(jobId, cancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // One failed renewal is not fatal; the next tick tries again before the lock lapses.
                _logger.LogWarning(ex, "Failed to auto-renew timeout for message {MessageId} on {QueueName}; retrying on the next interval",
                    message.Id, _options.QueueName);
            }
        }
    }

    private async Task HeartbeatAsync(QueueMessage message, string? jobId, CancellationToken ct)
    {
        await _client.RenewTimeoutAsync(message, _options.VisibilityTimeout, ct).ConfigureAwait(false);

        if (jobId is not null && _stateStore is not null)
            await TryUpdateStateAsync(() => _stateStore.HeartbeatAsync(jobId, ct)).ConfigureAwait(false);
    }

    private async Task UpdateJobProgressAsync(string jobId, int percent, string? message, CancellationToken ct)
    {
        if (_stateStore is null) return;

        // Cancellation checks must propagate, so this call is deliberately not wrapped.
        if (await _stateStore.IsCancellationRequestedAsync(jobId, ct).ConfigureAwait(false))
            throw new OperationCanceledException("Job cancellation was requested.");

        await TryUpdateStateAsync(() => _stateStore.UpdateJobProgressAsync(jobId, Math.Clamp(percent, 0, 100), message, _stateExpiry, ct)).ConfigureAwait(false);
        await TryUpdateStateAsync(() => _stateStore.HeartbeatAsync(jobId, ct)).ConfigureAwait(false);
    }

    private Task FailJobAsync(string? jobId, string errorMessage)
    {
        if (jobId is null || _stateStore is null)
            return Task.CompletedTask;

        return TryUpdateStateAsync(() => _stateStore.UpdateJobStatusAsync(jobId, QueueJobStatus.Failed, completedUtc: _timeProvider.GetUtcNow(), errorMessage: errorMessage, expiry: _stateExpiry, cancellationToken: AckToken()));
    }

    private async Task RecordFailureAsync(QueueMessage message, System.Diagnostics.TagList tags, TimeSpan elapsed)
    {
        if (_options.AutoComplete)
            await AbandonWithBackoffAsync(message).ConfigureAwait(false);

        _workerInfo?.Stats.IncrementFailed();
        DistributedMetrics.Failed.Add(1, tags);
        DistributedMetrics.HandlerDuration.Record(elapsed.TotalMilliseconds, DistributedMetrics.Tags(_options.QueueName, _options.MessageType.Name, _options.Group, "failed"));

        if (_stateStore is not null)
            await TryUpdateStateAsync(() => _stateStore.IncrementCounterAsync(_options.QueueName, "failed", 1, AckToken())).ConfigureAwait(false);
    }

    private async Task RecordDeadLetterAsync(QueueMessage message, string reason, string? jobId, System.Diagnostics.TagList tags)
    {
        _workerInfo?.Stats.IncrementDeadLettered();
        DistributedMetrics.DeadLettered.Add(1, tags);

        if (_stateStore is not null)
            await TryUpdateStateAsync(() => _stateStore.IncrementCounterAsync(_options.QueueName, "dead_lettered", 1, AckToken())).ConfigureAwait(false);

        try
        {
            await _client.DeadLetterAsync(message, reason, AckToken()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Without a dead-letter queue the message would otherwise reappear every visibility period; park it for the maximum delay instead.
            _logger.LogError(ex, "Failed to dead-letter message {MessageId} on {QueueName} ({Reason}); abandoning with maximum delay",
                message.Id, _options.QueueName, reason);
            await TryAckAsync(() => _client.AbandonAsync(message, QueueRetryDelay.MaxDelay, AckToken()), message, "abandon").ConfigureAwait(false);
        }
    }

    private Task AbandonWithBackoffAsync(QueueMessage message)
    {
        var delay = QueueRetryDelay.Compute(_options.RetryPolicy, _options.RetryDelay, message.DequeueCount, _options.RetrySchedule);
        return TryAckAsync(() => _client.AbandonAsync(message, delay, AckToken()), message, "abandon");
    }

    private Task AbandonForRedeliveryAsync(QueueMessage message, string reason)
    {
        DistributedMetrics.Abandoned.Add(1, DistributedMetrics.Tags(_options.QueueName, _options.MessageType.Name, _options.Group, reason));
        return TryAckAsync(() => _client.AbandonAsync(message, TimeSpan.Zero, AckToken()), message, "abandon");
    }

    private async Task TryAckAsync(Func<Task> operation, QueueMessage message, string operationName)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Operation} message {MessageId} on {QueueName}; the transport will redeliver it when the visibility timeout lapses",
                operationName, message.Id, _options.QueueName);
        }
    }

    /// <summary>
    /// Runs a job-state operation, logging instead of failing the message when the store is unavailable.
    /// </summary>
    private async Task TryUpdateStateAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update job state store for queue {QueueName}; message processing will continue", _options.QueueName);
        }
    }

    /// <summary>
    /// Acknowledgements of finished work must not observe the stopping token; they get their own bounded timeout.
    /// </summary>
    private static CancellationToken AckToken() => new CancellationTokenSource(s_ackTimeout).Token;

    /// <summary>
    /// Whether a failed <see cref="ResultStatus"/> is transient. Content errors are dead-lettered immediately.
    /// </summary>
    internal static bool IsRetryableStatus(ResultStatus status) => status switch
    {
        ResultStatus.Error => true,
        ResultStatus.Unavailable => true,
        ResultStatus.RateLimited => true,
        _ => false
    };
}
