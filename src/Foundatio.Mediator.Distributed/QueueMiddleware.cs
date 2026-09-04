using System.Diagnostics;
using System.Text.Json;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Middleware attached to <see cref="QueueAttribute"/> handlers. On the caller side it serializes
/// the message, sends it to the handler's queue, and returns <see cref="Result.Accepted()"/>.
/// On the worker side, where a <see cref="QueueContext"/> is present, it runs the handler.
/// </summary>
/// <remarks>
/// <para>When several handlers share a queue, only one of them (the designated enqueuer, chosen
/// deterministically) sends the message; the worker then dispatches the single message to every
/// handler that accepts it.</para>
/// <para>A notification re-published from the distributed bus is not enqueued again: the node that
/// published it already did.</para>
/// </remarks>
[Middleware(Order = -100, ExplicitOnly = true, Lifetime = MediatorLifetime.Singleton)]
public class QueueMiddleware
{
    private static readonly HashSet<string> s_voidReturnTypes = new(StringComparer.Ordinal)
    {
        "void", "Task", "ValueTask", "System.Threading.Tasks.Task", "System.Threading.Tasks.ValueTask"
    };

    private readonly IQueueClient _client;
    private readonly QueueTopology _topology;
    private readonly DistributedQueueOptions _options;
    private readonly IQueueJobStateStore? _stateStore;
    private readonly DistributedInfrastructureReady? _infraReady;
    private readonly IQueueHeaderProvider[] _headerProviders;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TimeProvider _timeProvider;

    public QueueMiddleware(
        IQueueClient client,
        QueueTopology topology,
        DistributedQueueOptions? options = null,
        IQueueJobStateStore? stateStore = null,
        DistributedInfrastructureReady? infraReady = null,
        IEnumerable<IQueueHeaderProvider>? headerProviders = null,
        TimeProvider? timeProvider = null)
    {
        _client = client;
        _topology = topology;
        _options = options ?? new DistributedQueueOptions();
        _stateStore = stateStore;
        _infraReady = infraReady;
        _headerProviders = headerProviders?.ToArray() ?? [];
        _jsonOptions = _options.JsonSerializerOptions ?? JsonSerializerOptions.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<object?> ExecuteAsync(
        object message,
        HandlerExecutionDelegate next,
        HandlerExecutionInfo handlerInfo,
        CallContext? callContext,
        CancellationToken cancellationToken)
    {
        // Worker side: the queue already owns this message, run the handler.
        if (callContext?.TryGet<QueueContext>(out _) == true)
            return await next().ConfigureAwait(false);

        // The originating node enqueued this notification before publishing it to the bus.
        if (DistributedContext.IsInboundNotification(message))
            return Result.Accepted("Message queued by the originating node");

        var registration = _topology.GetByDescriptorId(handlerInfo.DescriptorId)
            ?? throw new InvalidOperationException(
                $"Handler '{handlerInfo.DescriptorId}' is marked [Queue] but has no queue registration. Call AddDistributedQueues() after AddMediator().");

        var messageType = message.GetType();

        var designated = registration.DesignatedEnqueuerFor(messageType);
        if (designated is not null && !string.Equals(designated, handlerInfo.DescriptorId, StringComparison.Ordinal))
            return Result.Accepted("Message queued");

        ValidateReturnType(registration, handlerInfo.DescriptorId);

        await WaitForInfrastructureAsync(registration.QueueName, messageType, cancellationToken).ConfigureAwait(false);

        var body = JsonSerializer.SerializeToUtf8Bytes(message, messageType, _jsonOptions);
        var now = _timeProvider.GetUtcNow();

        var headers = new Dictionary<string, string>
        {
            [MessageHeaders.MessageType] = messageType.FullName!,
            [MessageHeaders.EnqueuedAt] = now.ToString("O"),
            [MessageHeaders.CorrelationId] = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
        };

        using var activity = MediatorActivitySource.Instance.StartActivity($"Enqueue {registration.QueueName}", ActivityKind.Producer);
        activity?.SetTag("messaging.operation.type", "send");
        activity?.SetTag("messaging.destination.name", registration.QueueName);
        activity?.SetTag("messaging.message.type", messageType.FullName);

        var traceActivity = activity ?? Activity.Current;
        if (traceActivity is not null)
        {
            headers[MessageHeaders.TraceParent] = traceActivity.Id!;
            if (traceActivity.TraceStateString is { Length: > 0 } traceState)
                headers[MessageHeaders.TraceState] = traceState;
        }

        foreach (var provider in _headerProviders)
            provider.Enrich(message, headers);

        string? jobId = null;
        if (registration.Settings.TrackProgress && _stateStore is not null)
        {
            jobId = Guid.NewGuid().ToString("N");
            headers[MessageHeaders.JobId] = jobId;

            var jobState = new QueueJobState
            {
                JobId = jobId,
                QueueName = registration.QueueName,
                MessageType = messageType.FullName ?? messageType.Name,
                Status = QueueJobStatus.Queued,
                CreatedUtc = now,
                LastUpdatedUtc = now,
                Metadata = _options.JobMetadataProvider?.Invoke(message)
            };

            await _stateStore.SetJobStateAsync(jobState, _options.JobStateExpiry, cancellationToken).ConfigureAwait(false);
            activity?.SetTag("messaging.job.id", jobId);
        }

        await _client.SendAsync(registration.QueueName, [new QueueEntry { Body = body, Headers = headers }], cancellationToken).ConfigureAwait(false);

        DistributedMetrics.Enqueued.Add(1, DistributedMetrics.Tags(registration.QueueName, messageType.Name, registration.Settings.Group));

        return jobId is not null
            ? Result.Accepted("Message queued", jobId)
            : Result.Accepted("Message queued");
    }

    private async Task WaitForInfrastructureAsync(string queueName, Type messageType, CancellationToken cancellationToken)
    {
        // Provisioning only runs once the host starts; before that (unit tests, plain containers) there is nothing to wait for.
        if (_infraReady is null || !_infraReady.IsStarted || _infraReady.IsReady)
            return;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.EnqueueReadyTimeout);

        try
        {
            await _infraReady.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Queue infrastructure was not ready within {_options.EnqueueReadyTimeout}; cannot enqueue {messageType.Name} to '{queueName}'.");
        }
    }

    private static void ValidateReturnType(QueueRegistration registration, string descriptorId)
    {
        HandlerRegistration? handler = null;
        foreach (var candidate in registration.Handlers)
        {
            if (string.Equals(candidate.DescriptorId, descriptorId, StringComparison.Ordinal))
            {
                handler = candidate;
                break;
            }
        }

        var returnTypeName = handler?.ReturnTypeName;
        if (string.IsNullOrEmpty(returnTypeName) || s_voidReturnTypes.Contains(returnTypeName!))
            return;

        if (!returnTypeName!.StartsWith("Foundatio.Mediator.Result", StringComparison.Ordinal) && !returnTypeName.StartsWith("Result", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Queue handler '{descriptorId}' returns '{returnTypeName}' which is incompatible with queue processing. " +
                "Queue handlers must return void, Task, Result, or Result<T>.");
        }
    }
}
