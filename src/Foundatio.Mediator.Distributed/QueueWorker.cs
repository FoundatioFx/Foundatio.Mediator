using Foundatio.Jobs;
using Foundatio.Messaging;
using Foundatio.Serializer;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Background service that processes messages from a single queue. A receive loop pulls batches
/// from the <see cref="IMessageBus"/> only when processing capacity is available. Each task deserializes its
/// message by its <see cref="KnownHeaders.MessageType"/> header and dispatch it to every handler
/// on the queue that accepts that type, through the normal middleware pipeline.
/// </summary>
/// <remarks>
/// On host stop the receive loop ends immediately, buffered messages are abandoned so another
/// worker picks them up, and in-flight handlers get <see cref="DistributedQueueOptions.ShutdownTimeout"/>
/// to finish before they are cancelled and their messages abandoned. Acknowledgements of finished
/// work use an independent timeout. Delivery remains at least once; handlers must tolerate retries.
/// </remarks>
public sealed class QueueWorker : BackgroundService
{

    private readonly IMessageBus _bus;
    private readonly MessageExecutionPipeline _pipeline;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly QueueWorkerOptions _options;
    private readonly QueueWorkerInfo? _workerInfo;
    private readonly DistributedInfrastructureReady? _infraReady;
    private readonly IMessageTypeRegistry _messageTypes;
    private readonly IQueueHeaderProvider[] _headerProviders;
    private readonly ISerializer _serializer;
    private readonly TimeSpan _receiveBatchDelay;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<QueueWorker> _logger;
    private readonly TimeSpan _shutdownTimeout;
    private readonly string _workerId;

    public QueueWorker(
        IMessageBus bus,
        IMessageTypeRegistry messageTypes,
        IServiceScopeFactory scopeFactory,
        QueueWorkerOptions options,
        DistributedQueueOptions? distributedOptions,
        ILogger<QueueWorker> logger,
        QueueWorkerInfo? workerInfo = null,
        IJobRuntimeStore? stateStore = null,
        DistributedInfrastructureReady? infraReady = null,
        TimeProvider? timeProvider = null,
        ISerializer? serializer = null,
        IEnumerable<IQueueHeaderProvider>? headerProviders = null)
    {
        _workerId = distributedOptions?.WorkerId ?? $"{Environment.MachineName}:{Environment.ProcessId}";
        _bus = bus;
        _scopeFactory = scopeFactory;
        _options = options;
        _workerInfo = workerInfo;
        _infraReady = infraReady;
        _messageTypes = messageTypes;
        _headerProviders = headerProviders?.ToArray() ?? [];
        _serializer = serializer ?? DefaultSerializer.Instance;
        _receiveBatchDelay = distributedOptions?.ReceiveBatchDelay ?? TimeSpan.FromMilliseconds(1);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
        _shutdownTimeout = distributedOptions?.ShutdownTimeout ?? TimeSpan.FromSeconds(30);
        _pipeline = new MessageExecutionPipeline(new MessageExecutionOptions
        {
            QueueName = options.QueueName,
            MessageType = options.MessageType,
            MaxAttempts = options.MaxAttempts,
            RetryBackoff = attempt => QueueRetryDelay.Compute(options.RetryPolicy, options.RetryDelay, attempt, options.RetrySchedule),
            VisibilityTimeout = options.VisibilityTimeout,
            AutoComplete = options.AutoComplete,
            TrackProgress = options.TrackProgress,
            ExecutionIdHeader = ExecutionHeaders.ExecutionId,
            WorkerId = _workerId,
            CancellationPollInterval = options.CancellationPollInterval,
            OnProcessed = RecordOutcome
        }, stateStore, _timeProvider, logger);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_infraReady is not null)
        {
            try { await _infraReady.WaitAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }

        _workerInfo?.Stats.SetRunning(true);

        try
        {
            await using var consumer = await _bus.ConsumeAsync((context, token) => _pipeline.ProcessAsync(context, DispatchAsync, token), new MessageConsumerOptions
            {
                Destination = _options.QueueName,
                MaxConcurrency = _options.Concurrency,
                PrefetchCount = _options.PrefetchCount,
                ReceiveBatchDelay = _receiveBatchDelay,
                VisibilityTimeout = _options.VisibilityTimeout,
                AutoRenewLock = _options.AutoRenewTimeout,
                ShutdownTimeout = _shutdownTimeout,
                MaxAttempts = _options.MaxAttempts,
                AckMode = AckMode.Manual,
                WaitForManualSettlement = false
            }, stoppingToken).ConfigureAwait(false);
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        finally { _workerInfo?.Stats.SetRunning(false); }
    }

    private async ValueTask<MessageOutcome> DispatchAsync(MessageProcessingContext processing, CancellationToken token)
    {
        var tags = DistributedMetrics.Tags(_options.QueueName, _options.MessageType.Name, _options.Group);
        long started = Stopwatch.GetTimestamp();
        bool succeeded = false;
        DistributedMetrics.InFlight.Add(1, tags);
        try
        {
            var messageType = ResolveMessageType(processing.Headers);
            if (messageType is null)
                return MessageOutcome.DeadLetter($"Unknown message type '{processing.Headers.GetValueOrDefault(KnownHeaders.MessageType) ?? "<none>"}'");
            object? message;
            try { message = _serializer.Deserialize(processing.Body, messageType); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return MessageOutcome.DeadLetter($"Deserialization failed for type {messageType.Name}: {exception.Message}");
            }
            if (message is null)
                return MessageOutcome.DeadLetter($"Deserialization returned null for type {messageType.Name}");
            var handlers = HandlersFor(messageType);
            if (handlers.Count == 0)
                return MessageOutcome.DeadLetter($"No handler accepts message type {messageType.Name}");
            await using var scope = _scopeFactory.CreateAsyncScope();
            using var callContext = CallContext.Rent().Set(processing);
            RestoreHeaders(processing.Headers, callContext, scope.ServiceProvider);
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            if (mediator is Mediator) mediator = Mediator.FromServiceProvider(scope.ServiceProvider);
            foreach (var handler in handlers)
            {
                token.ThrowIfCancellationRequested();
                var result = await handler.HandleAsync(mediator, message, callContext, token, typeof(object), skipAuthorization: true).ConfigureAwait(false);
                if (result is IResult { IsSuccess: false } failure)
                {
                    var reason = string.IsNullOrEmpty(failure.Message) ? $"Handler returned {failure.Status}" : failure.Message;
                    return IsRetryableStatus(failure.Status) ? MessageOutcome.Retry(reason) : MessageOutcome.DeadLetter($"{failure.Status}: {reason}");
                }
            }
            succeeded = !processing.IsAbandoned;
            return MessageOutcome.Success;
        }
        finally
        {
            DistributedMetrics.InFlight.Add(-1, tags);
            tags.Add("outcome", succeeded ? "processed" : "failed");
            DistributedMetrics.HandlerDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags);
        }
    }

    private void RecordOutcome(MessageOutcomeKind outcome, TimeSpan duration)
    {
        var tags = DistributedMetrics.Tags(_options.QueueName, _options.MessageType.Name, _options.Group);
        switch (outcome)
        {
            case MessageOutcomeKind.Success:
                _workerInfo?.Stats.IncrementProcessed();
                DistributedMetrics.Processed.Add(1, tags);
                break;
            case MessageOutcomeKind.Retry:
                _workerInfo?.Stats.IncrementFailed();
                DistributedMetrics.Failed.Add(1, tags);
                break;
            case MessageOutcomeKind.DeadLetter:
                _workerInfo?.Stats.IncrementDeadLettered();
                DistributedMetrics.DeadLettered.Add(1, tags);
                break;
        }
    }

    private Type? ResolveMessageType(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue(KnownHeaders.MessageType, out var typeName) || string.IsNullOrEmpty(typeName))
            return _options.MessageType;

        if (string.Equals(typeName, _options.MessageType.FullName, StringComparison.Ordinal) || string.Equals(typeName, _options.MessageType.AssemblyQualifiedName, StringComparison.Ordinal))
            return _options.MessageType;

        var resolved = _messageTypes.Resolve(typeName);
        return resolved is not null && _options.Registrations.Any(handler => handler.MessageType?.IsAssignableFrom(resolved) == true)
            ? resolved : null;
    }

    private IReadOnlyList<HandlerRegistration> HandlersFor(Type messageType)
    {
        if (_options.ResolveHandlers is { } resolve)
            return resolve(messageType);

        var handlers = new List<HandlerRegistration>(_options.Registrations.Count);
        foreach (var handler in _options.Registrations)
        {
            if (handler.MessageType is { } declared && declared.IsAssignableFrom(messageType))
                handlers.Add(handler);
        }

        return handlers;
    }

    private void RestoreHeaders(IReadOnlyDictionary<string, string> headers, CallContext callContext, IServiceProvider services)
    {
        foreach (var provider in _headerProviders.Concat(services.GetServices<IQueueHeaderProvider>()).Distinct())
            provider.Restore(headers, callContext);
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
