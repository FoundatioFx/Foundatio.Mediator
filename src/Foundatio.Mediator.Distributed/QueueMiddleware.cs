using System.Diagnostics;
using System.Text.Json;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Middleware that intercepts handler invocations for <see cref="QueueAttribute"/>-decorated handlers.
/// </summary>
/// <remarks>
/// <para>
/// On the <b>enqueue path</b> (normal caller), this middleware serializes the message
/// and sends it to the queue via <see cref="IQueueClient"/>.
/// The call returns immediately with <see cref="Result.Accepted(string)"/>.
/// </para>
/// <para>
/// On the <b>process path</b> (when <see cref="QueueWorker"/> dispatches a dequeued message),
/// the presence of a <see cref="QueueContext"/> in <see cref="CallContext"/>
/// signals that this is a processing invocation. The middleware passes through to <c>next()</c>
/// so the full pipeline (logging, validation, auth, etc.) executes before the handler.
/// </para>
/// </remarks>
[Middleware(Order = -100, ExplicitOnly = true)]
public class QueueMiddleware
{
    private readonly IQueueClient _client;
    private readonly IQueueJobStateStore? _stateStore;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TimeProvider _timeProvider;

    public QueueMiddleware(IQueueClient client, DistributedOptions? options = null, IQueueJobStateStore? stateStore = null, TimeProvider? timeProvider = null)
    {
        _client = client;
        _stateStore = stateStore;
        _jsonOptions = options?.JsonSerializerOptions ?? JsonSerializerOptions.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<object?> ExecuteAsync(
        object message,
        HandlerExecutionDelegate next,
        HandlerExecutionInfo handlerInfo,
        CallContext? callContext)
    {
        // Process path: QueueContext in CallContext signals we're processing from the queue
        if (callContext?.TryGet<QueueContext>(out _) == true)
            return await next().ConfigureAwait(false);

        // Inbound notification path: message arrived from the distributed bus.
        // The originating node already enqueued to the shared queue, so skip re-enqueueing.
        if (DistributedContext.IsNotification)
            return await next().ConfigureAwait(false);

        // Enqueue path: serialize and send to the queue
        var messageType = message.GetType();
        var body = JsonSerializer.SerializeToUtf8Bytes(message, messageType, _jsonOptions);
        var queueName = GetQueueName(handlerInfo, messageType);

        var headers = new Dictionary<string, string>
        {
            [MessageHeaders.MessageType] = messageType.AssemblyQualifiedName!,
            [MessageHeaders.EnqueuedAt] = _timeProvider.GetUtcNow().ToString("O")
        };

        // Propagate W3C trace context so queue consumers appear in the same trace
        var currentActivity = Activity.Current;
        if (currentActivity is not null)
        {
            headers[MessageHeaders.TraceParent] = currentActivity.Id!;
            if (currentActivity.TraceStateString is { Length: > 0 } traceState)
                headers[MessageHeaders.TraceState] = traceState;
        }

        // Generate job ID and track initial state when progress tracking is enabled
        string? jobId = null;
        var trackProgress = IsTrackProgressEnabled(handlerInfo);
        if (trackProgress && _stateStore is not null)
        {
            jobId = Guid.NewGuid().ToString("N");
            headers[MessageHeaders.JobId] = jobId;

            var now = _timeProvider.GetUtcNow();
            var jobState = new QueueJobState
            {
                JobId = jobId,
                QueueName = queueName,
                MessageType = messageType.FullName ?? messageType.Name,
                Status = QueueJobStatus.Queued,
                CreatedUtc = now,
                LastUpdatedUtc = now
            };

            await _stateStore.SetJobStateAsync(jobState, cancellationToken: default).ConfigureAwait(false);
        }

        var entry = new QueueEntry
        {
            Body = body,
            Headers = headers
        };

        await _client.SendAsync(queueName, entry, default).ConfigureAwait(false);

        if (jobId is not null)
            return Result.Accepted(jobId);

        return Result.Accepted("Message queued");
    }

    private static string GetQueueName(HandlerExecutionInfo handlerInfo, Type messageType)
    {
        var queueAttr = handlerInfo.HandlerType
            .GetCustomAttributes(typeof(QueueAttribute), true)
            .OfType<QueueAttribute>()
            .FirstOrDefault();

        return !string.IsNullOrWhiteSpace(queueAttr?.QueueName)
            ? queueAttr!.QueueName!
            : messageType.Name;
    }

    private static bool IsTrackProgressEnabled(HandlerExecutionInfo handlerInfo)
    {
        var queueAttr = handlerInfo.HandlerType
            .GetCustomAttributes(typeof(QueueAttribute), true)
            .OfType<QueueAttribute>()
            .FirstOrDefault();

        return queueAttr?.TrackProgress == true;
    }
}
