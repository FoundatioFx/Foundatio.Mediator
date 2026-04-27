using System.ComponentModel;

namespace Foundatio.Mediator;

/// <summary>
/// Default implementation of <see cref="IMediator"/>. Dispatches messages to registered handlers
/// using compile-time generated code for near-direct call performance.
/// </summary>
public sealed class Mediator : IMediator, IServiceProvider
{
    private readonly HandlerRegistry _registry;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IServiceProvider _serviceProvider;

    public Mediator(HandlerRegistry registry, INotificationPublisher notificationPublisher, IServiceProvider serviceProvider)
    {
        _registry = registry;
        _notificationPublisher = notificationPublisher;
        _serviceProvider = serviceProvider;

        registry.TryLogStartupInfo(serviceProvider);
    }

    /// <summary>
    /// Gets the handler registry. Used by generated code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public HandlerRegistry Registry => _registry;

    /// <inheritdoc />
    object? IServiceProvider.GetService(Type serviceType) => _serviceProvider.GetService(serviceType);

    /// <inheritdoc />
    public ValueTask InvokeAsync(object message, CancellationToken cancellationToken = default)
    {
        var handlerFunc = _registry.GetInvokeAsyncDelegate(message.GetType());
        return handlerFunc(this, message, cancellationToken);
    }

    /// <inheritdoc />
    public void Invoke(object message, CancellationToken cancellationToken = default)
    {
        var handlerFunc = _registry.GetInvokeDelegate(message.GetType());
        handlerFunc(this, message, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<TResponse> InvokeAsync<TResponse>(object message, CancellationToken cancellationToken = default)
    {
        var handlerFunc = _registry.GetInvokeAsyncResponseDelegate(message.GetType(), typeof(TResponse));
        object? result = await handlerFunc(this, message, cancellationToken);
        return (TResponse)result!;
    }

    /// <inheritdoc />
    public ValueTask<TResponse> InvokeAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        return InvokeAsync<TResponse>((object)request, cancellationToken);
    }

    /// <inheritdoc />
    public TResponse Invoke<TResponse>(object message, CancellationToken cancellationToken = default)
    {
        var handlerFunc = _registry.GetInvokeResponseDelegate(message.GetType(), typeof(TResponse));
        object? result = handlerFunc(this, message, cancellationToken);
        return (TResponse)result!;
    }

    /// <inheritdoc />
    public TResponse Invoke<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        return Invoke<TResponse>((object)request, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync(object message, CancellationToken cancellationToken = default)
    {
        if (_registry.HasSubscribers)
        {
            _registry.TryWriteSubscription(message);
            _registry.TryWriteSingleAsBatchSubscription(message);
        }

        var handlers = _registry.GetAllApplicableHandlers(message);
        return _notificationPublisher.PublishAsync(this, handlers, message, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync<T>(IEnumerable<T> messages, CancellationToken cancellationToken = default)
    {
        var list = messages as T[] ?? messages.ToArray();
        if (list.Length == 0)
            return default;

        // Notify streaming subscribers
        if (_registry.HasSubscribers)
        {
            // Individual T subscribers get each message
            for (int i = 0; i < list.Length; i++)
                _registry.TryWriteSubscription(list[i]!);

            // Batch subscribers (IReadOnlyList<T> / T[]) get the full batch
            _registry.TryWriteSubscription(list);
        }

        // Group messages by runtime type to ensure derived-type handlers fire correctly.
        // This mirrors PublishAsync(object) which uses message.GetType().
        return PublishGroupedByRuntimeTypeAsync(list, cancellationToken);
    }

    private async ValueTask PublishGroupedByRuntimeTypeAsync<T>(T[] messages, CancellationToken cancellationToken)
    {
        var groups = GroupMessagesByRuntimeType(messages);

        for (int g = 0; g < groups.Count; g++)
        {
            var (messageType, groupMessages) = groups[g];
            var handlers = _registry.GetOrderedHandlers(messageType);
            if (handlers.Length == 0)
                continue;

            // Check if we have only singles or only batches for optimized paths
            bool hasAnySingle = false, hasAnyBatch = false;
            for (int i = 0; i < handlers.Length; i++)
            {
                if (handlers[i].IsBatch) hasAnyBatch = true;
                else hasAnySingle = true;
                if (hasAnySingle && hasAnyBatch) break;
            }

            if (!hasAnyBatch)
            {
                // Only single handlers → use publisher strategy
                var delegates = ExtractSingleDelegates(handlers);
                for (int i = 0; i < groupMessages.Count; i++)
                    await _notificationPublisher.PublishAsync(this, delegates, groupMessages[i], cancellationToken).ConfigureAwait(false);
            }
            else if (!hasAnySingle)
            {
                // Only batch handlers → use publisher strategy
                var delegates = ExtractBatchDelegates(handlers);
                await _notificationPublisher.PublishBatchAsync(this, delegates, groupMessages, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Mixed → execute in sorted order to preserve cross-kind ordering
                await PublishMixedOrderedAsync(groupMessages, handlers, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask PublishMixedOrderedAsync(IReadOnlyList<object> messages, OrderedPublishHandler[] handlers, CancellationToken cancellationToken)
    {
        for (int h = 0; h < handlers.Length; h++)
        {
            var handler = handlers[h];
            if (handler.IsBatch)
            {
                await handler.PublishBatch!(this, messages, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                for (int i = 0; i < messages.Count; i++)
                    await handler.PublishSingle!(this, messages[i], cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static List<(Type MessageType, IReadOnlyList<object> Messages)> GroupMessagesByRuntimeType<T>(T[] messages)
    {
        // Fast path: check if all messages are the exact same type
        var firstType = messages[0]!.GetType();
        bool allSameType = true;
        for (int i = 1; i < messages.Length; i++)
        {
            if (messages[i]!.GetType() != firstType)
            {
                allSameType = false;
                break;
            }
        }

        if (allSameType)
        {
            var boxed = BoxMessages(messages);
            return [( firstType, boxed )];
        }

        var groups = new List<(Type MessageType, IReadOnlyList<object> Messages)>();
        var groupIndexes = new Dictionary<Type, int>();

        for (int i = 0; i < messages.Length; i++)
        {
            object message = messages[i]!;
            var messageType = message.GetType();

            if (!groupIndexes.TryGetValue(messageType, out var groupIndex))
            {
                groupIndex = groups.Count;
                groups.Add((messageType, new List<object>()));
                groupIndexes[messageType] = groupIndex;
            }

            ((List<object>)groups[groupIndex].Messages).Add(message);
        }

        return groups;
    }

    private static PublishAsyncDelegate[] ExtractSingleDelegates(OrderedPublishHandler[] handlers)
    {
        var delegates = new PublishAsyncDelegate[handlers.Length];
        for (int i = 0; i < handlers.Length; i++)
            delegates[i] = handlers[i].PublishSingle!;
        return delegates;
    }

    private static PublishBatchAsyncDelegate[] ExtractBatchDelegates(OrderedPublishHandler[] handlers)
    {
        var delegates = new PublishBatchAsyncDelegate[handlers.Length];
        for (int i = 0; i < handlers.Length; i++)
            delegates[i] = handlers[i].PublishBatch!;
        return delegates;
    }

    private static IReadOnlyList<object> BoxMessages<T>(T[] messages)
    {
        var boxed = new object[messages.Length];
        for (int i = 0; i < messages.Length; i++)
            boxed[i] = messages[i]!;
        return boxed;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<T> SubscribeAsync<T>(CancellationToken cancellationToken = default, SubscriberOptions? options = null)
    {
        return _registry.SubscribeAsync<T>(cancellationToken, options);
    }
}
