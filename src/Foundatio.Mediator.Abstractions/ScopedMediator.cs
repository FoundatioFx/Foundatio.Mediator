using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator;

/// <summary>
/// A mediator wrapper that carries a scoped IServiceProvider through the call chain.
/// This allows nested handler calls, middleware, and cascading messages to share the same DI scope
/// without using AsyncLocal, providing better performance.
/// Uses reference counting to avoid allocations when passed to nested handlers.
/// </summary>
public sealed class ScopedMediator : IMediator, IServiceProvider, IDisposable, IAsyncDisposable
{
    private readonly IMediator _rootMediator;
    private readonly IServiceProvider _scopedServiceProvider;
    private readonly IDisposable? _scope;
    private readonly bool _ownsScope;
    private int _refCount;

    /// <summary>
    /// Creates a ScopedMediator that owns and manages the provided scope.
    /// </summary>
    private ScopedMediator(IMediator rootMediator, IServiceScope scope)
    {
        _rootMediator = rootMediator is ScopedMediator sm ? sm._rootMediator : rootMediator;
        _scopedServiceProvider = scope.ServiceProvider;
        _scope = scope;
        _ownsScope = true;
        _refCount = 1;
    }

    /// <summary>
    /// Gets the scoped service provider. Also available via IServiceProvider.GetService.
    /// </summary>
    public IServiceProvider Services => _scopedServiceProvider;

    /// <inheritdoc />
    public object? GetService(Type serviceType) => _scopedServiceProvider.GetService(serviceType);

    /// <summary>
    /// Adds a reference to this scoped mediator. Call Release() when done.
    /// </summary>
    private void AddRef()
    {
        System.Threading.Interlocked.Increment(ref _refCount);
    }

    /// <summary>
    /// Gets or creates a scoped mediator. If already a ScopedMediator, increments ref count and returns the same instance.
    /// </summary>
    public static ScopedMediator GetOrCreateScope(IMediator mediator)
    {
        if (mediator is null) throw new ArgumentNullException(nameof(mediator));

        if (mediator is ScopedMediator scopedMediator)
        {
            scopedMediator.AddRef();
            return scopedMediator;
        }

        var serviceProvider = (IServiceProvider)mediator;
        return new ScopedMediator(mediator, serviceProvider.CreateScope());
    }

    /// <summary>
    /// Gets or creates a scoped mediator asynchronously. If already a ScopedMediator, increments ref count and returns the same instance.
    /// </summary>
    public static ScopedMediator GetOrCreateAsyncScope(IMediator mediator)
    {
        if (mediator is null) throw new ArgumentNullException(nameof(mediator));

        if (mediator is ScopedMediator scopedMediator)
        {
            scopedMediator.AddRef();
            return scopedMediator;
        }

        var serviceProvider = (IServiceProvider)mediator;
        return new ScopedMediator(mediator, serviceProvider.CreateAsyncScope());
    }

    /// <inheritdoc />
    public ValueTask InvokeAsync(object message, CancellationToken cancellationToken = default)
    {
        if (_rootMediator is Mediator mediator)
            return mediator.InvokeAsyncWithMediator(this, message, cancellationToken);

        return _rootMediator.InvokeAsync(message, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<TResponse> InvokeAsync<TResponse>(object message, CancellationToken cancellationToken = default)
    {
        if (_rootMediator is Mediator mediator)
            return mediator.InvokeAsyncWithMediator<TResponse>(this, message, cancellationToken);

        return _rootMediator.InvokeAsync<TResponse>(message, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<TResponse> InvokeAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        return InvokeAsync<TResponse>((object)request, cancellationToken);
    }

    /// <inheritdoc />
    public void Invoke(object message, CancellationToken cancellationToken = default)
    {
        if (_rootMediator is Mediator mediator)
        {
            mediator.InvokeWithMediator(this, message, cancellationToken);
            return;
        }
        _rootMediator.Invoke(message, cancellationToken);
    }

    /// <inheritdoc />
    public TResponse Invoke<TResponse>(object message, CancellationToken cancellationToken = default)
    {
        if (_rootMediator is Mediator mediator)
            return mediator.InvokeWithMediator<TResponse>(this, message, cancellationToken);

        return _rootMediator.Invoke<TResponse>(message, cancellationToken);
    }

    /// <inheritdoc />
    public TResponse Invoke<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        return Invoke<TResponse>((object)request, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask PublishAsync(object message, CancellationToken cancellationToken = default)
    {
        if (_rootMediator is Mediator mediator)
            return mediator.PublishAsyncWithMediator(this, message, cancellationToken);

        return _rootMediator.PublishAsync(message, cancellationToken);
    }

    public void Dispose()
    {
        int remaining = System.Threading.Interlocked.Decrement(ref _refCount);
        if (remaining == 0 && _ownsScope)
            _scope?.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        int remaining = System.Threading.Interlocked.Decrement(ref _refCount);
        if (remaining == 0 && _ownsScope && _scope is not null)
        {
            if (_scope is IAsyncDisposable asyncDisposable)
                return asyncDisposable.DisposeAsync();

            _scope.Dispose();
        }
        return default;
    }
}
