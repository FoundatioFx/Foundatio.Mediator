using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Middleware attached to <see cref="QueueAttribute"/> handlers. On the caller side it serializes
/// the message, sends it to the handler's queue, and returns <see cref="Result.Accepted()"/>.
/// On the worker side, where a <see cref="QueueContext"/> is present, it runs the handler.
/// </summary>
/// <remarks>
/// <para>When handlers explicitly share a queue, the registry selects one matching enqueue pipeline
/// per publication. The worker processes all matching handlers in that group.</para>
/// <para>A notification re-published from the distributed bus is not enqueued again: the node that
/// published it already did.</para>
/// </remarks>
[Middleware(ExplicitOnly = true, Lifetime = MediatorLifetime.Singleton, IsDispatcher = true)]
public class QueueMiddleware
{
    private readonly IQueueClient _client;
    private readonly QueueTopology _topology;
    private readonly DistributedQueueOptions _options;
    private readonly IQueueJobStateStore? _stateStore;
    private readonly DistributedInfrastructureReady? _infraReady;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TimeProvider _timeProvider;

    public QueueMiddleware(
        IQueueClient client,
        QueueTopology topology,
        DistributedQueueOptions? options = null,
        IQueueJobStateStore? stateStore = null,
        DistributedInfrastructureReady? infraReady = null,
        TimeProvider? timeProvider = null)
    {
        _client = client;
        _topology = topology;
        _options = options ?? new DistributedQueueOptions();
        _stateStore = stateStore;
        _infraReady = infraReady;
        _jsonOptions = _options.JsonSerializerOptions ?? JsonSerializerOptions.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<object?> ExecuteAsync(
        object message,
        HandlerExecutionDelegate next,
        HandlerExecutionInfo handlerInfo,
        CallContext? callContext,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        // The originating node enqueued this notification before publishing it to the bus.
        if (DistributedContext.IsInboundNotification(message))
            return Result.Accepted("Message queued by the originating node");

        var registration = _topology.GetByDescriptorId(handlerInfo.DescriptorId)
            ?? throw new InvalidOperationException(
                $"Handler '{handlerInfo.DescriptorId}' is marked [Queue] but has no queue registration. Call AddDistributedQueues() after AddMediator().");

        var messageType = message.GetType();

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

        foreach (var provider in services.GetServices<IQueueHeaderProvider>())
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

        var receipt = new QueueReceipt(registration.QueueName, jobId);
        try
        {
            await _client.SendAsync(registration.QueueName, [new QueueEntry { Body = body, Headers = headers }], cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (jobId is not null && _stateStore is not null)
            {
                try
                {
                    await QueueOperation.RunAsync(ct => _stateStore.UpdateJobStatusAsync(jobId, QueueJobStatus.EnqueueUnknown,
                        errorMessage: exception.Message, expiry: _options.JobStateExpiry, cancellationToken: ct),
                        TimeSpan.FromSeconds(5), _timeProvider).ConfigureAwait(false);
                }
                catch { /* Preserve the transport failure and receipt even when state is unavailable. */ }
            }
            throw new QueueEnqueueException(receipt, exception);
        }
        if (callContext?.Get(typeof(QueueReceiptCapture)) is QueueReceiptCapture capture)
            capture.Receipt = receipt;

        DistributedMetrics.Enqueued.Add(1, DistributedMetrics.Tags(registration.QueueName, messageType.Name, registration.Settings.Group));

        return Result.Accepted("Message queued");
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

}
