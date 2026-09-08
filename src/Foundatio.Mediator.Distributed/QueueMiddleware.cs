using Foundatio.Jobs;
using Foundatio.Messaging;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Middleware attached to <see cref="QueueAttribute"/> handlers. On the caller side it serializes
/// the message, sends it to the handler's queue, and returns <see cref="Result.Accepted()"/>.
/// On the worker side, where a <see cref="MessageProcessingContext"/> is present, it runs the handler.
/// </summary>
/// <remarks>
/// <para>When handlers explicitly share a queue, this middleware sends only from the first matching
/// registration in Mediator's existing publication order. The worker processes the matching group.</para>
/// <para>A notification re-published from the distributed bus is not enqueued again: the node that
/// published it already did.</para>
/// </remarks>
[Middleware(ExplicitOnly = true, Lifetime = MediatorLifetime.Singleton, Order = int.MaxValue)]
public class QueueMiddleware
{
    private readonly IMessageBus _bus;
    private readonly QueueTopology _topology;
    private readonly DistributedQueueOptions _options;
    private readonly IJobRuntimeStore? _stateStore;
    private readonly DistributedInfrastructureReady? _infraReady;
    private readonly TimeProvider _timeProvider;

    public QueueMiddleware(
        IMessageBus bus,
        QueueTopology topology,
        DistributedQueueOptions? options = null,
        IJobRuntimeStore? stateStore = null,
        DistributedInfrastructureReady? infraReady = null,
        TimeProvider? timeProvider = null)
    {
        _topology = topology;
        _options = options ?? new DistributedQueueOptions();
        _stateStore = stateStore;
        _infraReady = infraReady;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _bus = bus;
    }

    public async ValueTask<HandlerResult> BeforeAsync(
        object message,
        HandlerExecutionInfo handlerInfo,
        CallContext? callContext,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        if (callContext?.Get(typeof(MessageProcessingContext)) is MessageProcessingContext)
            return HandlerResult.Continue();

        // The originating node enqueued this notification before publishing it to the bus.
        if (DistributedContext.IsInboundNotification(message))
            return HandlerResult.ShortCircuit(Result.Accepted("Message queued by the originating node"));

        var registration = _topology.GetByDescriptorId(handlerInfo.DescriptorId)
            ?? throw new InvalidOperationException(
                $"Handler '{handlerInfo.DescriptorId}' is marked [Queue] but has no queue registration. Call AddDistributedQueues() after AddMediator().");

        var messageType = message.GetType();
        if (registration.HandlersFor(messageType)[0].DescriptorId != handlerInfo.DescriptorId)
            return HandlerResult.ShortCircuit(Result.Accepted("Message queued by the subscription owner"));

        await WaitForInfrastructureAsync(registration.QueueName, messageType, cancellationToken).ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow();

        var headers = new Dictionary<string, string>
        {
            [KnownHeaders.MessageType] = messageType.FullName!,
            [ExecutionHeaders.EnqueuedAt] = now.ToString("O"),
            [KnownHeaders.CorrelationId] = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N")
        };

        using var activity = MediatorActivitySource.Instance.StartActivity($"Enqueue {registration.QueueName}", ActivityKind.Producer);
        activity?.SetTag("messaging.operation.type", "send");
        activity?.SetTag("messaging.destination.name", registration.QueueName);
        activity?.SetTag("messaging.message.type", messageType.FullName);

        var traceActivity = activity ?? Activity.Current;
        if (traceActivity is not null)
        {
            headers[KnownHeaders.TraceParent] = traceActivity.Id!;
            if (traceActivity.TraceStateString is { Length: > 0 } traceState)
                headers[KnownHeaders.TraceState] = traceState;
        }

        foreach (var provider in services.GetServices<IQueueHeaderProvider>())
            provider.Enrich(message, headers);

        string? jobId = null;
        if (registration.Settings.TrackProgress && _stateStore is not null)
        {
            jobId = Guid.NewGuid().ToString("N");
            headers[ExecutionHeaders.ExecutionId] = jobId;

            var jobState = new JobState
            {
                JobId = jobId,
                Name = registration.QueueName,
                ExecutionOwner = JobExecutionOwner.Broker,
                HistoryRetention = _options.JobStateExpiry,
                QueueName = registration.QueueName,
                PayloadType = messageType.FullName ?? messageType.Name,
                Status = JobStatus.Queued,
                CreatedUtc = now,
                LastUpdatedUtc = now,
                Metadata = _options.JobMetadataProvider?.Invoke(message)
            };

            await _stateStore.CreateIfAbsentAsync(jobState, cancellationToken).ConfigureAwait(false);
            activity?.SetTag("messaging.job.id", jobId);
        }

        var receipt = new QueueReceipt(registration.QueueName, jobId);
        try
        {
            await _bus.SendAsync(message, new MessageSendOptions
            {
                Destination = registration.QueueName,
                Headers = Foundatio.Messaging.MessageHeaders.Create(headers)
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            if (jobId is not null && _stateStore is not null)
            {
                try
                {
                    await QueueOperation.RunAsync(ct => _stateStore.MarkEnqueueUnknownAsync(jobId, exception.Message, ct),
                        TimeSpan.FromSeconds(5), _timeProvider).ConfigureAwait(false);
                }
                catch { /* Preserve the transport failure and receipt even when state is unavailable. */ }
            }
            throw new QueueEnqueueException(receipt, exception);
        }
        if (callContext?.Get(typeof(QueueReceiptCapture)) is QueueReceiptCapture capture)
            capture.Receipt = receipt;

        DistributedMetrics.Enqueued.Add(1, DistributedMetrics.Tags(registration.QueueName, messageType.Name, registration.Settings.Group));

        return HandlerResult.ShortCircuit(Result.Accepted("Message queued"));
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
