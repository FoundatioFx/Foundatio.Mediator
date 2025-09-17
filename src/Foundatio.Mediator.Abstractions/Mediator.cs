using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator;

public class Mediator : IMediator, IServiceProvider
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MediatorConfiguration _configuration;

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
        var handlerFunc = GetInvokeAsyncDelegate(message.GetType());
        return handlerFunc(this, message, cancellationToken);
    }

    public void Invoke(object message, CancellationToken cancellationToken = default)
    {
        var handlerFunc = GetInvokeDelegate(message.GetType());
        handlerFunc(this, message, cancellationToken);
    }

    public async ValueTask<TResponse> InvokeAsync<TResponse>(object message, CancellationToken cancellationToken = default)
    {
        var handlerFunc = GetInvokeAsyncResponseDelegate(message.GetType(), typeof(TResponse));
        var result = await handlerFunc(this, message, cancellationToken);
        return (TResponse)result!;
    }

    public TResponse Invoke<TResponse>(object message, CancellationToken cancellationToken = default)
    {
        var handlerFunc = GetInvokeResponseDelegate(message.GetType(), typeof(TResponse));
        var result = handlerFunc(this, message, cancellationToken);
        return (TResponse)result!;
    }

    public ValueTask PublishAsync(object message, CancellationToken cancellationToken = default)
    {
        var handlersList = GetAllApplicableHandlers(message).ToList();
        return _configuration.NotificationPublisher.PublishAsync(this, handlersList, message, cancellationToken);
    }

    public void ShowRegisteredHandlers()
    {
        var logger = _serviceProvider.GetRequiredService<ILogger<Mediator>>();
        var registrations = _serviceProvider.GetServices<HandlerRegistration>().ToArray();
        if (!registrations.Any())
        {
            logger.LogInformation("No handlers registered.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Registered Handlers:");
        foreach (var registration in registrations.OrderBy(r => r.MessageTypeName).ThenBy(r => r.HandlerClassName))
        {
            sb.AppendLine($"- Message: {registration.MessageTypeName}, Handler: {registration.HandlerClassName}, IsAsync: {registration.IsAsync}");
        }

        logger.LogInformation(sb.ToString());
    }

    [DebuggerStepThrough]
    private InvokeAsyncDelegate GetInvokeAsyncDelegate(Type messageType)
    {
        return _invokeAsyncCache.GetOrAdd(messageType, mt =>
        {
            var handlers = GetHandlersForType(mt);
            var handlersList = handlers.ToList();

            if (handlersList.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {MessageTypeKey.Get(mt)}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {MessageTypeKey.Get(mt)}. Use PublishAsync for multiple handlers.");

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
                throw new InvalidOperationException($"No handler found for message type {MessageTypeKey.Get(mt)}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {MessageTypeKey.Get(mt)}. Use Publish for multiple handlers.");

            var handler = handlersList.First();
            if (handler.IsAsync)
                throw new InvalidOperationException($"Cannot use synchronous Invoke with async-only handler for message type {MessageTypeKey.Get(mt)}. Use InvokeAsync instead.");

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
                throw new InvalidOperationException($"No handler found for message type {MessageTypeKey.Get(key.MessageType)}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {MessageTypeKey.Get(key.MessageType)}. Use PublishAsync for multiple handlers.");

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
                throw new InvalidOperationException($"No handler found for message type {MessageTypeKey.Get(key.MessageType)}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {MessageTypeKey.Get(key.MessageType)}. Use Publish for multiple handlers.");

            var handler = handlersList.First();
            if (handler.IsAsync)
                throw new InvalidOperationException($"Cannot use synchronous Invoke with async-only handler for message type {MessageTypeKey.Get(key.MessageType)}. Use InvokeAsync instead.");

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
                .Distinct()
                .Select<HandlerRegistration, PublishAsyncDelegate>(h => async (mediator, msg, cancellationToken) => await h.HandleAsync(mediator, msg, cancellationToken, null))
                .ToArray();
        });
    }

    [DebuggerStepThrough]
    private IEnumerable<HandlerRegistration> GetHandlersForType(Type type)
    {
        var list = _serviceProvider.GetKeyedServices<HandlerRegistration>(MessageTypeKey.Get(type)).ToList();
        if (!type.IsGenericType)
            return list;

        var genericDefinition = type.GetGenericTypeDefinition();
        var registration = _openGenericClosedCache.GetOrAdd(type, t =>
        {
            var descriptors = _serviceProvider.GetServices<OpenGenericHandlerDescriptor>();
            foreach (var descriptor in descriptors)
            {
                if (descriptor.MessageTypeGenericDefinition == genericDefinition)
                {
                    return ConstructClosedRegistration(t, descriptor);
                }
            }

            return null;
        });

        if (registration != null)
            list.Add(registration);

        return list;
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
    private static readonly ConcurrentDictionary<Type, HandlerRegistration?> _openGenericClosedCache = new();

    private HandlerRegistration? ConstructClosedRegistration(Type closedMessageType, OpenGenericHandlerDescriptor descriptor)
    {
        try
        {
            var typeArgs = closedMessageType.GetGenericArguments();
            var wrapperClosed = descriptor.WrapperGenericTypeDefinition.MakeGenericType(typeArgs);
            var asyncMethod = wrapperClosed.GetMethod("UntypedHandleAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (asyncMethod == null)
                return null;

            HandleAsyncDelegate asyncDelegate = (IMediator mediator, object message, CancellationToken ct, Type? returnType) =>
            {
                var taskObj = asyncMethod.Invoke(null, new object?[] { mediator, message, ct, returnType });
                return taskObj is ValueTask<object?> vt ? vt : (ValueTask<object?>)taskObj!;
            };

            HandleDelegate? syncDelegate = null;
            if (!descriptor.IsAsync)
            {
                var syncMethod = wrapperClosed.GetMethod("UntypedHandle", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (syncMethod != null)
                {
                    syncDelegate = (IMediator mediator, object message, CancellationToken ct, Type? returnType) =>
                    {
                        return syncMethod.Invoke(null, new object?[] { mediator, message, ct, returnType });
                    };
                }
            }

            return new HandlerRegistration(MessageTypeKey.Get(closedMessageType), wrapperClosed.FullName ?? wrapperClosed.Name, asyncDelegate, syncDelegate, descriptor.IsAsync);
        }
        catch
        {
            return null;
        }
    }

}
