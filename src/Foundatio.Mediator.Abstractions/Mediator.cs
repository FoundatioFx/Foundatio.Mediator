using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator;

public class Mediator : IMediator, IServiceProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MediatorConfiguration _configuration;

#if !DISABLE_MEDIATOR_OPENTELEMETRY
    private static readonly ActivitySource ActivitySource = new("Foundatio.Mediator");
#endif

    [DebuggerStepThrough]
    public Mediator(IServiceProvider serviceProvider, MediatorConfiguration? configuration = null)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration ?? new MediatorConfiguration();
    }

    public IServiceProvider ServiceProvider => _serviceProvider;
    public object? GetService(Type serviceType) => _serviceProvider.GetService(serviceType);

    public ValueTask InvokeAsync(object message, CancellationToken cancellationToken = default)
    {
#if !DISABLE_MEDIATOR_OPENTELEMETRY
        using var activity = ActivitySource.StartActivity("mediator.invoke");
        if (activity != null)
        {
            var messageType = message.GetType();
            activity.SetTag("messaging.operation", "invoke");
            activity.SetTag("messaging.message_type", messageType.FullName);
        }
#endif
        var handlerFunc = GetInvokeAsyncDelegate(message.GetType());
        return handlerFunc(this, message, cancellationToken);
    }

    public void Invoke(object message, CancellationToken cancellationToken = default)
    {
#if !DISABLE_MEDIATOR_OPENTELEMETRY
        using var activity = ActivitySource.StartActivity("mediator.invoke");
        if (activity != null)
        {
            var messageType = message.GetType();
            activity.SetTag("messaging.operation", "invoke");
            activity.SetTag("messaging.message_type", messageType.FullName);
        }
#endif
        var handlerFunc = GetInvokeDelegate(message.GetType());
        handlerFunc(this, message, cancellationToken);
    }

    public async ValueTask<TResponse> InvokeAsync<TResponse>(object message, CancellationToken cancellationToken = default)
    {
#if !DISABLE_MEDIATOR_OPENTELEMETRY
        using var activity = ActivitySource.StartActivity("mediator.invoke");
        if (activity != null)
        {
            var messageType = message.GetType();
            activity.SetTag("messaging.operation", "invoke");
            activity.SetTag("messaging.message_type", messageType.FullName);
            activity.SetTag("messaging.response_type", typeof(TResponse).FullName);
        }
#endif
        var handlerFunc = GetInvokeAsyncResponseDelegate(message.GetType(), typeof(TResponse));
        var result = await handlerFunc(this, message, cancellationToken);
        return (TResponse)result!;
    }

    public TResponse Invoke<TResponse>(object message, CancellationToken cancellationToken = default)
    {
#if !DISABLE_MEDIATOR_OPENTELEMETRY
        using var activity = ActivitySource.StartActivity("mediator.invoke");
        if (activity != null)
        {
            var messageType = message.GetType();
            activity.SetTag("messaging.operation", "invoke");
            activity.SetTag("messaging.message_type", messageType.FullName);
            activity.SetTag("messaging.response_type", typeof(TResponse).FullName);
        }
#endif
        var handlerFunc = GetInvokeResponseDelegate(message.GetType(), typeof(TResponse));
        var result = handlerFunc(this, message, cancellationToken);
        return (TResponse)result!;
    }

    public ValueTask PublishAsync(object message, CancellationToken cancellationToken = default)
    {
        var handlersList = GetAllApplicableHandlers(message);
#if !DISABLE_MEDIATOR_OPENTELEMETRY
        using var activity = ActivitySource.StartActivity("mediator.publish");
        if (activity != null)
        {
            var messageType = message.GetType();
            activity.SetTag("messaging.operation", "publish");
            activity.SetTag("messaging.message_type", messageType.FullName);
            activity.SetTag("messaging.handler_count", handlersList.Length);
        }
#endif
        return _configuration.NotificationPublisher.PublishAsync(this, handlersList.ToList(), message, cancellationToken);
    }

    [DebuggerStepThrough]
    private InvokeAsyncDelegate GetInvokeAsyncDelegate(Type messageType)
    {
        return _invokeAsyncCache.GetOrAdd(messageType, mt =>
        {
            var handlers = GetHandlersForType(mt);
            var handlersList = handlers.ToList();

            if (handlersList.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {mt.FullName}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {mt.FullName}. Use PublishAsync for multiple handlers.");

            var handler = handlersList.First();
            return async (mediator, msg, ct) => await handler.HandleAsync(mediator, msg, ct, null);
        });
    }

    [DebuggerStepThrough]
    private InvokeDelegate GetInvokeDelegate(Type messageType)
    {
        return _invokeCache.GetOrAdd(messageType, mt =>
        {
            var handlers = GetHandlersForType(mt);
            var handlersList = handlers.ToList();

            if (handlersList.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {mt.FullName}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {mt.FullName}. Use Publish for multiple handlers.");

            var handler = handlersList.First();
            if (handler.IsAsync)
                throw new InvalidOperationException($"Cannot use synchronous Invoke with async-only handler for message type {mt.FullName}. Use InvokeAsync instead.");

            return (mediator, msg, ct) => handler.Handle!(mediator, msg, ct, null);
        });
    }

    [DebuggerStepThrough]
    private InvokeAsyncResponseDelegate GetInvokeAsyncResponseDelegate(Type messageType, Type responseType)
    {
        return _invokeAsyncWithResponseCache.GetOrAdd((messageType, responseType), key =>
        {
            var handlers = GetHandlersForType(key.MessageType);
            var handlersList = handlers.ToList();

            if (handlersList.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {key.MessageType.FullName}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {key.MessageType.FullName}. Use PublishAsync for multiple handlers.");

            var handler = handlersList.First();
            return (mediator, msg, ct) => handler.HandleAsync(mediator, msg, ct, key.ResponseType);
        });
    }

    [DebuggerStepThrough]
    private InvokeResponseDelegate GetInvokeResponseDelegate(Type messageType, Type responseType)
    {
        return _invokeWithResponseCache.GetOrAdd((messageType, responseType), key =>
        {
            var handlers = GetHandlersForType(key.MessageType);
            var handlersList = handlers.ToList();

            if (handlersList.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {key.MessageType.FullName}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {key.MessageType.FullName}. Use Publish for multiple handlers.");

            var handler = handlersList.First();
            if (handler.IsAsync)
                throw new InvalidOperationException($"Cannot use synchronous Invoke with async-only handler for message type {key.MessageType.FullName}. Use InvokeAsync instead.");

            return (mediator, msg, ct) => handler.Handle!(mediator, msg, ct, key.ResponseType);
        });
    }

    [DebuggerStepThrough]
    private PublishAsyncDelegate[] GetAllApplicableHandlers(object message)
    {
        var messageType = message.GetType();

        return _publishCache.GetOrAdd(messageType, mt =>
        {
            var allHandlers = new List<HandlerRegistration>();

            // Add handlers for the exact message type
            var exactHandlers = GetHandlersForType(messageType);
            allHandlers.AddRange(exactHandlers);

            // Add handlers for all implemented interfaces
            foreach (var interfaceType in messageType.GetInterfaces())
            {
                var interfaceHandlers = GetHandlersForType(interfaceType);
                allHandlers.AddRange(interfaceHandlers);
            }

            // Add handlers for all base classes
            var currentType = messageType.BaseType;
            while (currentType != null && currentType != typeof(object))
            {
                var baseHandlers = GetHandlersForType(currentType);
                allHandlers.AddRange(baseHandlers);
                currentType = currentType.BaseType;
            }

            return allHandlers
                .GroupBy(h => $"{h.MessageTypeName}:{h.HandleAsync.Method.DeclaringType?.FullName}:{h.HandleAsync.Method.Name}")
                .Select(g => g.First())
                .Select<HandlerRegistration, PublishAsyncDelegate>(h => async (mediator, msg, cancellationToken) => await h.HandleAsync(mediator, msg, cancellationToken, null))
                .ToArray();
        });
    }

    [DebuggerStepThrough]
    private IEnumerable<HandlerRegistration> GetHandlersForType(Type type)
    {
        return _serviceProvider.GetKeyedServices<HandlerRegistration>(type.FullName);
    }

    private static readonly ConcurrentDictionary<Type, object> _middlewareCache = new();

    [DebuggerStepThrough]
    public static T GetOrCreateMiddleware<T>(IServiceProvider serviceProvider) where T : class
    {
        // Check cache first - if it's there, it means it's not registered in DI
        if (_middlewareCache.TryGetValue(typeof(T), out var cachedInstance))
            return (T)cachedInstance;

        // Try to get from DI - if registered, always use DI (respects service lifetime)
        var middlewareFromDI = serviceProvider.GetService<T>();
        if (middlewareFromDI != null)
            return middlewareFromDI;

        // Not in DI, create and cache our own instance
        return (T)_middlewareCache.GetOrAdd(typeof(T), type =>
            ActivatorUtilities.CreateInstance<T>(serviceProvider));
    }

    private delegate ValueTask InvokeAsyncDelegate(IMediator mediator, object message, CancellationToken cancellationToken);
    private static readonly ConcurrentDictionary<Type, InvokeAsyncDelegate> _invokeAsyncCache = new();

    private delegate void InvokeDelegate(IMediator mediator, object message, CancellationToken cancellationToken);
    private static readonly ConcurrentDictionary<Type, InvokeDelegate> _invokeCache = new();

    private delegate ValueTask<object?> InvokeAsyncResponseDelegate(IMediator mediator, object message, CancellationToken cancellationToken);
    private static readonly ConcurrentDictionary<(Type MessageType, Type ResponseType), InvokeAsyncResponseDelegate> _invokeAsyncWithResponseCache = new();

    private delegate object? InvokeResponseDelegate(IMediator mediator, object message, CancellationToken cancellationToken);
    private static readonly ConcurrentDictionary<(Type MessageType, Type ResponseType), InvokeResponseDelegate> _invokeWithResponseCache = new();

    private static readonly ConcurrentDictionary<Type, PublishAsyncDelegate[]> _publishCache = new();

}
