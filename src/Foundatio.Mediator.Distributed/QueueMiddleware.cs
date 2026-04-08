using System.Collections.Concurrent;
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
    private readonly HandlerRegistry _registry;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TimeProvider _timeProvider;
    private readonly string? _resourcePrefix;
    private readonly ConcurrentDictionary<string, QueueHandlerMetadata> _metadataCache = new(StringComparer.Ordinal);

    public QueueMiddleware(IQueueClient client, HandlerRegistry registry, DistributedQueueOptions? options = null, IQueueJobStateStore? stateStore = null, TimeProvider? timeProvider = null)
    {
        _client = client;
        _registry = registry;
        _stateStore = stateStore;
        _jsonOptions = options?.JsonSerializerOptions ?? JsonSerializerOptions.Default;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _resourcePrefix = options?.ResourcePrefix;
    }

    public async ValueTask<object?> ExecuteAsync(
        object message,
        HandlerExecutionDelegate next,
        HandlerExecutionInfo handlerInfo,
        CallContext? callContext,
        CancellationToken cancellationToken)
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
        var metadata = GetMetadata(handlerInfo.DescriptorId, messageType);

        // Validate that the handler's declared return type is compatible with queue processing.
        // Queue handlers can only return void/Task/ValueTask, Result, or Result<T>.
        // This must be checked before sending to avoid enqueueing messages for incompatible handlers.
        if (!string.IsNullOrEmpty(metadata.ReturnTypeName)
            && !metadata.ReturnTypeName.StartsWith("Foundatio.Mediator.Result", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Queue handler '{handlerInfo.DescriptorId}' returns '{metadata.ReturnTypeName}' which is incompatible with queue processing. " +
                "Queue handlers must return void, Task, Result, or Result<T>.");
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(message, messageType, _jsonOptions);

        var headers = new Dictionary<string, string>
        {
            [MessageHeaders.MessageType] = messageType.FullName!,
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
        if (metadata.TrackProgress && _stateStore is not null)
        {
            jobId = Guid.NewGuid().ToString("N");
            headers[MessageHeaders.JobId] = jobId;

            var now = _timeProvider.GetUtcNow();
            var jobState = new QueueJobState
            {
                JobId = jobId,
                QueueName = metadata.QueueName,
                MessageType = messageType.FullName ?? messageType.Name,
                Status = QueueJobStatus.Queued,
                CreatedUtc = now,
                LastUpdatedUtc = now
            };

            await _stateStore.SetJobStateAsync(jobState, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var entry = new QueueEntry
        {
            Body = body,
            Headers = headers
        };

        await _client.SendAsync(metadata.QueueName, entry, cancellationToken).ConfigureAwait(false);

        if (jobId is not null)
            return Result.Accepted("Message queued", jobId);

        return Result.Accepted("Message queued");
    }

    private QueueHandlerMetadata GetMetadata(string descriptorId, Type messageType)
    {
        return _metadataCache.GetOrAdd(descriptorId, static (id, state) =>
        {
            var (registry, fallbackName, prefix) = state;
            string queueName;
            bool trackProgress;
            string? returnTypeName = null;

            if (registry.TryGetHandlerByDescriptorId(id, out var registration) && registration is not null)
            {
                var queueAttr = registration.GetPreferredAttribute<QueueAttribute>()?.Attribute as QueueAttribute;
                queueName = !string.IsNullOrWhiteSpace(queueAttr?.QueueName) ? queueAttr!.QueueName! : fallbackName;
                trackProgress = queueAttr?.TrackProgress ?? false;
                returnTypeName = registration.ReturnTypeName;
            }
            else
            {
                queueName = fallbackName;
                trackProgress = false;
            }

            // Apply resource prefix for app-level scoping
            if (!string.IsNullOrEmpty(prefix))
                queueName = $"{prefix}-{queueName}";

            return new QueueHandlerMetadata(queueName, trackProgress, returnTypeName);
        }, (_registry, messageType.Name, _resourcePrefix));
    }

    private sealed record QueueHandlerMetadata(string QueueName, bool TrackProgress, string? ReturnTypeName);
}
