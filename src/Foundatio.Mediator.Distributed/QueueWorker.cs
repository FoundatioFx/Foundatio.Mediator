using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Background service that processes messages from a single queue. A receive loop pulls batches
/// from the <see cref="IQueueClient"/> only when processing capacity is available. Each task deserializes its
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
    private readonly TimeSpan _receiveBatchDelay;
    private readonly TimeSpan _stateExpiry;
    private readonly string _workerId;

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
        _workerId = distributedOptions?.WorkerId ?? $"{Environment.MachineName}:{Environment.ProcessId}";
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
        _receiveBatchDelay = distributedOptions?.ReceiveBatchDelay ?? TimeSpan.FromMilliseconds(1);
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

            var active = new List<Task>(_options.Concurrency);
            try
            {
                await RunReceiveLoopAsync(active, stoppingToken, drainCts.Token).ConfigureAwait(false);
            }
            finally
            {
                await Task.WhenAll(active).ConfigureAwait(false);
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

    private async Task RunReceiveLoopAsync(List<Task> active, CancellationToken stoppingToken, CancellationToken drainToken)
    {
        int consecutiveErrors = 0;
        int largestReceivedBatch = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            active.RemoveAll(task => task.IsCompleted);
            if (active.Count >= _options.Concurrency)
            {
                try { await Task.WhenAny(active).WaitAsync(stoppingToken).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                active.RemoveAll(task => task.IsCompleted);
            }

            // Collect capacity released by related batch acknowledgments, including after an
            // underfilled broker receive. Sparse work and capacity for an observed full batch skip
            // this delay; a slow handler cannot extend it. Learn the transport's batch size without
            // assuming every distributed provider has SQS's ten-message receive limit.
            if (active.Count >= (_options.Concurrency + 1) / 2
                && _options.Concurrency - active.Count < largestReceivedBatch
                && _client.IsDistributed && _receiveBatchDelay > TimeSpan.Zero)
            {
                await Task.WhenAny(Task.WhenAll(active), Task.Delay(_receiveBatchDelay, _timeProvider, stoppingToken)).ConfigureAwait(false);
                if (stoppingToken.IsCancellationRequested) break;
                active.RemoveAll(task => task.IsCompleted);
            }

            IReadOnlyList<QueueMessage> messages;
            try
            {
                int capacity = Math.Min(_options.PrefetchCount, _options.Concurrency - active.Count);
                var receive = _client.ReceiveAsync(_options.QueueName, capacity, _options.VisibilityTimeout, stoppingToken);
                try
                {
                    messages = await receive.WaitAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _ = AbandonLateReceiveAsync(receive);
                    break;
                }
                consecutiveErrors = 0;
                largestReceivedBatch = Math.Max(largestReceivedBatch, messages.Count);
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

            foreach (var message in messages)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    await AbandonForRedeliveryAsync(message, "shutdown").ConfigureAwait(false);
                    (_client as IQueueProcessingObserver)?.ProcessingFinished(message);
                }
                else
                    active.Add(ProcessMessageAsync(message, drainToken));
            }
        }
    }

    private async Task AbandonLateReceiveAsync(Task<IReadOnlyList<QueueMessage>> receive)
    {
        try
        {
            foreach (var message in await receive.ConfigureAwait(false))
            {
                await AbandonForRedeliveryAsync(message, "late-shutdown-receive").ConfigureAwait(false);
                (_client as IQueueProcessingObserver)?.ProcessingFinished(message);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Receive completed after shutdown for {QueueName}", _options.QueueName);
        }
    }

    private async Task ProcessMessageAsync(QueueMessage message, CancellationToken drainToken)
    {
        await using var lease = new QueueLease(_client, message, _options.VisibilityTimeout,
            _options.AutoRenewTimeout, _timeProvider, _logger, drainToken);
        // Install every receipt's monitor before allowing user code to occupy a thread.
        await Task.Yield();
        try
        {
            await ProcessMessageCoreAsync(message, lease, drainToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error processing {MessageId} on {QueueName}", message.Id, _options.QueueName);
            if (!lease.IsLost)
                await AbandonForRedeliveryAsync(message, "worker-error").ConfigureAwait(false);
        }
        finally
        {
            (_client as IQueueProcessingObserver)?.ProcessingFinished(message);
        }
    }

    private async Task ProcessMessageCoreAsync(QueueMessage message, QueueLease lease, CancellationToken drainToken)
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

        string? jobId = null;
        var trackProgress = _options.TrackProgress && _stateStore is not null;
        if (trackProgress)
            message.Headers.TryGetValue(MessageHeaders.JobId, out jobId);
        if (jobId is not null)
            activity?.SetTag("messaging.job.id", jobId);

        if (_options.MaxAttempts >= 0 && message.DequeueCount > _options.MaxAttempts)
        {
            _logger.LogWarning(
                "Message {MessageId} on {QueueName} exceeded max attempts ({DequeueCount}/{MaxAttempts}), dead-lettering",
                message.Id, _options.QueueName, message.DequeueCount, _options.MaxAttempts);

            activity?.SetTag("messaging.dead_letter.reason", "MaxAttemptsExceeded");
            await RecordDeadLetterAsync(message, $"Exceeded max attempts ({_options.MaxAttempts})", jobId, tags).ConfigureAwait(false);
            return;
        }

        var messageType = ResolveMessageType(message);
        if (messageType is null)
        {
            var typeName = message.Headers.GetValueOrDefault(MessageHeaders.MessageType) ?? "<none>";
            _logger.LogWarning("Message {MessageId} on {QueueName} has unknown type '{TypeName}', dead-lettering", message.Id, _options.QueueName, typeName);
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
            await RecordDeadLetterAsync(message, $"Deserialization failed for type {messageType.Name}: {ex.Message}", jobId, tags).ConfigureAwait(false);
            return;
        }

        if (typedMessage is null)
        {
            _logger.LogWarning("Message {MessageId} on {QueueName} deserialized to null as {MessageType}, dead-lettering", message.Id, _options.QueueName, messageType.Name);
            await RecordDeadLetterAsync(message, $"Deserialization returned null for type {messageType.Name}", jobId, tags).ConfigureAwait(false);
            return;
        }

        var handlers = HandlersFor(messageType);
        if (handlers.Count == 0)
        {
            _logger.LogWarning("No handler on {QueueName} accepts message type {MessageType}, dead-lettering {MessageId}", _options.QueueName, messageType.Name, message.Id);
            await RecordDeadLetterAsync(message, $"No handler accepts message type {messageType.Name}", jobId, tags).ConfigureAwait(false);
            return;
        }

        Task? cancellationPollTask = null;
        var handlerToken = lease.Token;
        var stopwatch = Stopwatch.StartNew();
        DistributedMetrics.InFlight.Add(1, tags);
        QueueContext? queueContext = null;

        try
        {
            if (jobId is not null && await ReadCancellationAsync(jobId, handlerToken).ConfigureAwait(false))
            {
                if (await TryAckAsync(ct => _client.CompleteAsync(message, ct), message, "cancel").ConfigureAwait(false))
                    await SetJobStatusAsync(jobId, QueueJobStatus.Cancelled, message.DequeueCount).ConfigureAwait(false);
                return;
            }
            if (jobId is not null)
                cancellationPollTask = PollForCancellationAsync(jobId, message.DequeueCount, lease);

            if (jobId is not null)
            {
                bool accepted = await QueueOperation.RunAsync(ct => _stateStore!.UpdateJobStatusAsync(jobId, QueueJobStatus.Processing,
                    startedUtc: _timeProvider.GetUtcNow(), attempt: message.DequeueCount, expiry: _stateExpiry, cancellationToken: ct, workerId: _workerId),
                    s_ackTimeout, _timeProvider, handlerToken).ConfigureAwait(false);
                if (!accepted)
                {
                    var current = await QueueOperation.RunAsync(ct => _stateStore!.GetJobStateAsync(jobId, ct), s_ackTimeout, _timeProvider, handlerToken).ConfigureAwait(false);
                    if (current?.Status is QueueJobStatus.Completed or QueueJobStatus.Failed or QueueJobStatus.Cancelled)
                        await TryAckAsync(ct => _client.CompleteAsync(message, ct), message, "settle-terminal-job").ConfigureAwait(false);
                    else
                        await AbandonWithBackoffAsync(message).ConfigureAwait(false);
                    return;
                }
            }

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
                Headers = message.Headers,
                OnSettled = lease.StopRenewing,
                OnCancelProcessing = lease.Cancel,
                OnRenewTimeout = lease.RenewAsync,
                OnReportProgress = ct => HeartbeatAsync(lease, jobId, message.DequeueCount, ct),
                OnReportDetailedProgress = jobId is not null
                    ? (percent, msg, ct) => UpdateJobProgressAsync(jobId, message.DequeueCount, percent, msg, ct)
                    : null,
                OnComplete = ct => QueueOperation.RunAsync(t => _client.CompleteAsync(message, t), s_ackTimeout, _timeProvider, ct),
                OnAbandon = (delay, ct) => QueueOperation.RunAsync(t => _client.AbandonAsync(message, delay, t), s_ackTimeout, _timeProvider, ct)
            };

            await using var scope = _scopeFactory.CreateAsyncScope();
            using var callContext = CallContext.Rent().Set(queueContext).Set(HandlerDispatchContext.Processing);
            RestoreHeaders(message, callContext, scope.ServiceProvider);
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            if (mediator is Mediator)
                mediator = Mediator.FromServiceProvider(scope.ServiceProvider);

            IResult? failure = null;
            foreach (var handler in handlers)
            {
                handlerToken.ThrowIfCancellationRequested();
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

                    await RecordFailureAsync(message, jobId, errorMessage, tags, stopwatch.Elapsed).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogWarning("Handler returned non-retryable status {Status} for message {MessageId} on {QueueName}: {Message}",
                        failure.Status, message.Id, _options.QueueName, errorMessage);

                    await RecordDeadLetterAsync(message, $"{failure.Status}: {errorMessage}", jobId, tags).ConfigureAwait(false);
                }

                return;
            }

            if (queueContext.IsAbandoned)
                return;

            handlerToken.ThrowIfCancellationRequested();
            if (_options.AutoComplete && !queueContext.IsCompleted)
                await TryAckAsync(queueContext.CompleteAsync, message, "complete").ConfigureAwait(false);

            if (!queueContext.IsCompleted)
            {
                await SetJobStatusAsync(jobId, QueueJobStatus.RetryPending, message.DequeueCount,
                    "Handler finished without confirmed acknowledgment; delivery may recur.").ConfigureAwait(false);
                return;
            }


        }
        catch (OperationCanceledException) when (drainToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogInformation("Host stopping, abandoning in-flight message {MessageId} on {QueueName} for redelivery", message.Id, _options.QueueName);
            activity?.SetStatus(ActivityStatusCode.Error, "shutdown");
            if (!lease.IsLost && queueContext is not { IsCompleted: true } and not { IsAbandoned: true })
            {
                await AbandonForRedeliveryAsync(message, "shutdown").ConfigureAwait(false);
                await SetJobStatusAsync(jobId, QueueJobStatus.RetryPending, message.DequeueCount).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            if (lease.IsLost)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "lease-lost");
                // Ownership passed to another worker. Do not settle or update its attempt.
                return;
            }
            if (queueContext is { IsCompleted: true } or { IsAbandoned: true })
                return;
            bool cancelled = jobId is not null && await ReadCancellationAsync(jobId, CancellationToken.None).ConfigureAwait(false);
            if (cancelled)
            {
                if (await TryAckAsync(ct => _client.CompleteAsync(message, ct), message, "cancel").ConfigureAwait(false))
                    await SetJobStatusAsync(jobId, QueueJobStatus.Cancelled, message.DequeueCount).ConfigureAwait(false);
            }
            else
                await RecordFailureAsync(message, jobId, "Processing was cancelled", tags, stopwatch.Elapsed).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error processing message {MessageId} on {QueueName} (attempt {DequeueCount}/{MaxAttempts})",
                message.Id, _options.QueueName, message.DequeueCount, _options.MaxAttempts);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            if (!lease.IsLost && queueContext is not { IsCompleted: true } and not { IsAbandoned: true })
                await RecordFailureAsync(message, jobId, ex.Message, tags, stopwatch.Elapsed).ConfigureAwait(false);
        }
        finally
        {
            DistributedMetrics.InFlight.Add(-1, tags);
            lease.StopRenewing();
            lease.Cancel();

            if (cancellationPollTask is not null)
                await cancellationPollTask.ConfigureAwait(false);

            // Explicit settlement remains authoritative even if later handler code throws.
            if (queueContext is { IsCompleted: true })
            {
                _workerInfo?.Stats.IncrementProcessed();
                DistributedMetrics.Processed.Add(1, tags);
                DistributedMetrics.HandlerDuration.Record(stopwatch.Elapsed.TotalMilliseconds,
                    DistributedMetrics.Tags(_options.QueueName, _options.MessageType.Name, _options.Group, "processed"));
                if (jobId is not null)
                    await TryUpdateStateAsync(ct => _stateStore!.UpdateJobStatusAsync(jobId, QueueJobStatus.Completed,
                        attempt: message.DequeueCount, completedUtc: _timeProvider.GetUtcNow(), progress: 100,
                        expiry: _stateExpiry, cancellationToken: ct)).ConfigureAwait(false);
                if (_stateStore is not null)
                    await TryUpdateStateAsync(ct => _stateStore.IncrementCounterAsync(_options.QueueName, "processed", 1, ct)).ConfigureAwait(false);
            }
            else if (queueContext is { IsAbandoned: true })
            {
                await SetJobStatusAsync(jobId, QueueJobStatus.RetryPending, message.DequeueCount).ConfigureAwait(false);
                DistributedMetrics.Abandoned.Add(1, tags);
            }
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

    private void RestoreHeaders(QueueMessage message, CallContext callContext, IServiceProvider services)
    {
        foreach (var provider in _headerProviders.Concat(services.GetServices<IQueueHeaderProvider>()).Distinct())
            provider.Restore(message.Headers, callContext);
    }

    private Task<bool> ReadCancellationAsync(string jobId, CancellationToken token)
        => QueueOperation.RunAsync(ct => _stateStore!.IsCancellationRequestedAsync(jobId, ct), s_ackTimeout, _timeProvider, token);

    private async Task PollForCancellationAsync(string jobId, int attempt, QueueLease lease)
    {
        var token = lease.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.CancellationPollInterval, _timeProvider, token).ConfigureAwait(false);
                if (await ReadCancellationAsync(jobId, token).ConfigureAwait(false))
                {
                    lease.Cancel();
                    return;
                }
                await TryUpdateStateAsync(ct => _stateStore!.HeartbeatAsync(jobId, ct, attempt, _stateExpiry), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to poll job {JobId}; will retry", jobId);
            }
        }
    }

    private async Task HeartbeatAsync(QueueLease lease, string? jobId, int attempt, CancellationToken ct)
    {
        await lease.RenewAsync(_options.VisibilityTimeout, ct).ConfigureAwait(false);
        if (jobId is not null)
            await TryUpdateStateAsync(token => _stateStore!.HeartbeatAsync(jobId, token, attempt, _stateExpiry), ct).ConfigureAwait(false);
    }

    private async Task UpdateJobProgressAsync(string jobId, int attempt, int percent, string? message, CancellationToken ct)
    {
        if (_stateStore is null) return;

        // Cancellation checks must propagate, so this call is deliberately not wrapped.
        if (await ReadCancellationAsync(jobId, ct).ConfigureAwait(false))
            throw new OperationCanceledException("Job cancellation was requested.");

        await TryUpdateStateAsync(ackToken => _stateStore.UpdateJobProgressAsync(jobId, Math.Clamp(percent, 0, 100), message, _stateExpiry, ackToken, attempt), ct).ConfigureAwait(false);
        await TryUpdateStateAsync(ackToken => _stateStore.HeartbeatAsync(jobId, ackToken, attempt, _stateExpiry), ct).ConfigureAwait(false);
    }

    private Task SetJobStatusAsync(string? jobId, QueueJobStatus status, int attempt, string? error = null)
        => jobId is null ? Task.CompletedTask : TryUpdateStateAsync(ct => _stateStore!.UpdateJobStatusAsync(jobId, status,
            attempt: attempt, completedUtc: status is QueueJobStatus.Completed or QueueJobStatus.Failed or QueueJobStatus.Cancelled ? _timeProvider.GetUtcNow() : null,
            errorMessage: error, expiry: _stateExpiry, cancellationToken: ct));

    private async Task RecordFailureAsync(QueueMessage message, string? jobId, string error, System.Diagnostics.TagList tags, TimeSpan elapsed)
    {
        if (_options.AutoComplete && _options.MaxAttempts > 0 && message.DequeueCount >= _options.MaxAttempts)
            await RecordDeadLetterAsync(message, error, jobId, tags).ConfigureAwait(false);
        else
        {
            if (_options.AutoComplete)
                await AbandonWithBackoffAsync(message).ConfigureAwait(false);
            await SetJobStatusAsync(jobId, QueueJobStatus.RetryPending, message.DequeueCount, error).ConfigureAwait(false);
        }

        _workerInfo?.Stats.IncrementFailed();
        DistributedMetrics.Failed.Add(1, tags);
        DistributedMetrics.HandlerDuration.Record(elapsed.TotalMilliseconds, DistributedMetrics.Tags(_options.QueueName, _options.MessageType.Name, _options.Group, "failed"));
        if (_stateStore is not null)
            await TryUpdateStateAsync(ct => _stateStore.IncrementCounterAsync(_options.QueueName, "failed", 1, ct)).ConfigureAwait(false);
    }

    private async Task RecordDeadLetterAsync(QueueMessage message, string reason, string? jobId, System.Diagnostics.TagList tags)
    {
        if (!await TryAckAsync(ct => _client.DeadLetterAsync(message, reason, ct), message, "dead-letter").ConfigureAwait(false))
        {
            await SetJobStatusAsync(jobId, QueueJobStatus.RetryPending, message.DequeueCount, reason).ConfigureAwait(false);
            return;
        }
        _workerInfo?.Stats.IncrementDeadLettered();
        DistributedMetrics.DeadLettered.Add(1, tags);
        await SetJobStatusAsync(jobId, QueueJobStatus.Failed, message.DequeueCount, reason).ConfigureAwait(false);
        if (_stateStore is not null)
            await TryUpdateStateAsync(ct => _stateStore.IncrementCounterAsync(_options.QueueName, "dead_lettered", 1, ct)).ConfigureAwait(false);
    }

    private Task AbandonWithBackoffAsync(QueueMessage message)
    {
        var delay = QueueRetryDelay.Compute(_options.RetryPolicy, _options.RetryDelay, message.DequeueCount, _options.RetrySchedule);
        return TryAckAsync(ackToken => _client.AbandonAsync(message, delay, ackToken), message, "abandon");
    }

    private Task AbandonForRedeliveryAsync(QueueMessage message, string reason)
    {
        DistributedMetrics.Abandoned.Add(1, DistributedMetrics.Tags(_options.QueueName, _options.MessageType.Name, _options.Group, reason));
        return TryAckAsync(ackToken => _client.AbandonAsync(message, TimeSpan.Zero, ackToken), message, "abandon");
    }

    private async Task<bool> TryAckAsync(Func<CancellationToken, Task> operation, QueueMessage message, string operationName)
    {
        try
        {
            await QueueOperation.RunAsync(operation, s_ackTimeout, _timeProvider).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Operation} message {MessageId} on {QueueName}; the transport will redeliver it when the visibility timeout lapses",
                operationName, message.Id, _options.QueueName);
            return false;
        }
    }

    /// <summary>
    /// Runs a job-state operation, logging instead of failing the message when the store is unavailable.
    /// </summary>
    private Task TryUpdateStateAsync(Func<CancellationToken, Task<bool>> operation, CancellationToken cancellationToken = default)
        => TryUpdateStateAsync(async ct => { _ = await operation(ct).ConfigureAwait(false); }, cancellationToken);

    private async Task TryUpdateStateAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
    {
        try
        {
            await QueueOperation.RunAsync(operation, s_ackTimeout, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update job state store for queue {QueueName}; message processing will continue", _options.QueueName);
        }
    }

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
