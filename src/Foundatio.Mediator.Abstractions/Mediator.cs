using System.ComponentModel;
using System.Diagnostics;

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

    [DebuggerStepThrough]
    public Mediator(HandlerRegistry registry, INotificationPublisher notificationPublisher, IServiceProvider serviceProvider)
    {
        _registry = registry;
        _notificationPublisher = notificationPublisher;
        _serviceProvider = serviceProvider;
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
            _registry.TryWriteSubscription(message);

        var handlers = _registry.GetAllApplicableHandlers(message);
        return _notificationPublisher.PublishAsync(this, handlers, message, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<T> SubscribeAsync<T>(CancellationToken cancellationToken = default, SubscriberOptions? options = null)
    {
        return _registry.SubscribeAsync<T>(cancellationToken, options);
    }
}
