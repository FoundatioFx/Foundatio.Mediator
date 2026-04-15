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

        var (singleHandlers, batchHandlers) = _registry.GetPartitionedHandlers(typeof(T));

        // Both empty → nothing to do
        if (singleHandlers.Length == 0 && batchHandlers.Length == 0)
            return default;

        // Only single handlers → dispatch each message individually
        if (batchHandlers.Length == 0)
            return PublishToSingleHandlersAsync(list, singleHandlers, cancellationToken);

        // Only batch handlers → dispatch once with the full batch
        if (singleHandlers.Length == 0)
        {
            var boxed = BoxMessages(list);
            return _notificationPublisher.PublishBatchAsync(this, batchHandlers, boxed, cancellationToken);
        }

        // Mixed → dispatch to both
        return PublishMixedAsync(list, singleHandlers, batchHandlers, cancellationToken);
    }

    private async ValueTask PublishToSingleHandlersAsync<T>(T[] messages, PublishAsyncDelegate[] handlers, CancellationToken cancellationToken)
    {
        for (int i = 0; i < messages.Length; i++)
            await _notificationPublisher.PublishAsync(this, handlers, messages[i]!, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask PublishMixedAsync<T>(T[] messages, PublishAsyncDelegate[] singleHandlers, PublishBatchAsyncDelegate[] batchHandlers, CancellationToken cancellationToken)
    {
        // Single handlers: per-message
        for (int i = 0; i < messages.Length; i++)
            await _notificationPublisher.PublishAsync(this, singleHandlers, messages[i]!, cancellationToken).ConfigureAwait(false);

        // Batch handlers: full batch
        var boxed = BoxMessages(messages);
        await _notificationPublisher.PublishBatchAsync(this, batchHandlers, boxed, cancellationToken).ConfigureAwait(false);
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
