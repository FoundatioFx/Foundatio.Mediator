using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundatio.Mediator;

/// <summary>
/// Central registry for all handler registrations. Populated at startup during
/// <c>AddMediator</c> and frozen (made immutable) before the application runs.
/// </summary>
public sealed class HandlerRegistry
{
    private readonly Dictionary<string, List<HandlerRegistration>> _handlersByMessageType = new();
    private readonly List<OpenGenericHandlerDescriptor> _openGenericDescriptors = new();
    private readonly List<HandlerRegistration> _allRegistrations = new();
    private bool _frozen;

    private readonly ConcurrentDictionary<Type, InvokeAsyncDelegate> _invokeAsyncCache = new();
    private readonly ConcurrentDictionary<Type, InvokeDelegate> _invokeCache = new();
    private readonly ConcurrentDictionary<(Type MessageType, Type ResponseType), InvokeAsyncResponseDelegate> _invokeAsyncWithResponseCache = new();
    private readonly ConcurrentDictionary<(Type MessageType, Type ResponseType), InvokeResponseDelegate> _invokeWithResponseCache = new();
    private readonly ConcurrentDictionary<Type, PublishAsyncDelegate[]> _publishCache = new();
    private readonly ConcurrentDictionary<Type, HandlerRegistration?> _openGenericClosedCache = new();

    /// <summary>
    /// Adds a handler registration to the registry. Must be called before <see cref="Freeze"/>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AddHandler(HandlerRegistration registration)
    {
        if (_frozen) throw new InvalidOperationException("Cannot add handlers after the registry has been frozen.");

        if (!_handlersByMessageType.TryGetValue(registration.MessageTypeName, out var list))
        {
            list = new List<HandlerRegistration>();
            _handlersByMessageType[registration.MessageTypeName] = list;
        }

        list.Add(registration);
        _allRegistrations.Add(registration);
    }

    /// <summary>
    /// Adds an open generic handler descriptor. Must be called before <see cref="Freeze"/>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AddOpenGenericHandler(OpenGenericHandlerDescriptor descriptor)
    {
        if (_frozen) throw new InvalidOperationException("Cannot add handlers after the registry has been frozen.");

        _openGenericDescriptors.Add(descriptor);
    }

    /// <summary>
    /// Freezes the registry so no more handlers can be added. Called after all modules are scanned.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void Freeze()
    {
        _frozen = true;
    }

    /// <summary>
    /// Gets all handler registrations in the registry.
    /// </summary>
    public IReadOnlyList<HandlerRegistration> Registrations => _allRegistrations;

    /// <summary>
    /// Gets all open generic handler descriptors in the registry.
    /// </summary>
    public IReadOnlyList<OpenGenericHandlerDescriptor> OpenGenericDescriptors => _openGenericDescriptors;

    /// <summary>
    /// Logs all registered handlers to the provided logger.
    /// </summary>
    public void ShowRegisteredHandlers(ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        if (_allRegistrations.Count == 0)
        {
            logger.LogInformation("No handlers registered.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Registered Handlers:");
        foreach (var registration in _allRegistrations.OrderBy(r => r.MessageTypeName).ThenBy(r => r.HandlerClassName))
        {
            sb.AppendLine($"- Message: {registration.MessageTypeName}, Handler: {registration.HandlerClassName}, IsAsync: {registration.IsAsync}");
        }

        logger.LogInformation(sb.ToString());
    }

    internal List<HandlerRegistration> GetHandlersForType(Type type)
    {
        var key = MessageTypeKey.Get(type);
        var list = _handlersByMessageType.TryGetValue(key, out var handlers)
            ? new List<HandlerRegistration>(handlers)
            : new List<HandlerRegistration>();

        if (!type.IsGenericType)
            return list;

        var genericDefinition = type.GetGenericTypeDefinition();
        var registration = _openGenericClosedCache.GetOrAdd(type, t =>
        {
            foreach (var descriptor in _openGenericDescriptors)
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

    [DebuggerStepThrough]
    internal InvokeAsyncDelegate GetInvokeAsyncDelegate(Type messageType)
    {
        return _invokeAsyncCache.GetOrAdd(messageType, mt =>
        {
            var handlersList = GetHandlersForType(mt);

            if (handlersList.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {MessageTypeKey.Get(mt)}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {MessageTypeKey.Get(mt)}. Use PublishAsync for multiple handlers.");

            var handler = handlersList[0];
            return async (mediator, msg, ct) => await handler.HandleAsync(mediator, msg, ct, null);
        });
    }

    [DebuggerStepThrough]
    internal InvokeDelegate GetInvokeDelegate(Type messageType)
    {
        return _invokeCache.GetOrAdd(messageType, mt =>
        {
            var handlersList = GetHandlersForType(mt);

            if (handlersList.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {MessageTypeKey.Get(mt)}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {MessageTypeKey.Get(mt)}. Use PublishAsync for multiple handlers.");

            var handler = handlersList[0];
            if (handler.IsAsync)
                throw new InvalidOperationException($"Cannot use synchronous Invoke with async-only handler for message type {MessageTypeKey.Get(mt)}. Use InvokeAsync instead.");

            return (mediator, msg, ct) => handler.Handle!(mediator, msg, ct, null);
        });
    }

    [DebuggerStepThrough]
    internal InvokeAsyncResponseDelegate GetInvokeAsyncResponseDelegate(Type messageType, Type responseType)
    {
        return _invokeAsyncWithResponseCache.GetOrAdd((messageType, responseType), key =>
        {
            var handlersList = GetHandlersForType(key.MessageType);

            if (handlersList.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {MessageTypeKey.Get(key.MessageType)}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {MessageTypeKey.Get(key.MessageType)}. Use PublishAsync for multiple handlers.");

            var handler = handlersList[0];
            return (mediator, msg, ct) => handler.HandleAsync(mediator, msg, ct, key.ResponseType);
        });
    }

    [DebuggerStepThrough]
    internal InvokeResponseDelegate GetInvokeResponseDelegate(Type messageType, Type responseType)
    {
        return _invokeWithResponseCache.GetOrAdd((messageType, responseType), key =>
        {
            var handlersList = GetHandlersForType(key.MessageType);

            if (handlersList.Count == 0)
                throw new InvalidOperationException($"No handler found for message type {MessageTypeKey.Get(key.MessageType)}");

            if (handlersList.Count > 1)
                throw new InvalidOperationException($"Multiple handlers found for message type {MessageTypeKey.Get(key.MessageType)}. Use PublishAsync for multiple handlers.");

            var handler = handlersList[0];
            if (handler.IsAsync)
                throw new InvalidOperationException($"Cannot use synchronous Invoke with async-only handler for message type {MessageTypeKey.Get(key.MessageType)}. Use InvokeAsync instead.");

            return (mediator, msg, ct) => handler.Handle!(mediator, msg, ct, key.ResponseType);
        });
    }

    [DebuggerStepThrough]
    internal PublishAsyncDelegate[] GetAllApplicableHandlers(object message)
    {
        var messageType = message.GetType();

        if (_publishCache.TryGetValue(messageType, out var cachedHandlers))
            return cachedHandlers;

        return BuildAndCachePublishHandlers(messageType);
    }

    /// <summary>
    /// Gets all publish handlers for a message type. Used by generated code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public PublishAsyncDelegate[] GetPublishHandlersForType(Type messageType)
    {
        if (_publishCache.TryGetValue(messageType, out var cachedHandlers))
            return cachedHandlers;

        return BuildAndCachePublishHandlers(messageType);
    }

    private PublishAsyncDelegate[] BuildAndCachePublishHandlers(Type messageType)
    {
        var allHandlers = new List<HandlerRegistration>();
        allHandlers.AddRange(GetHandlersForType(messageType));

        foreach (var interfaceType in messageType.GetInterfaces())
        {
            allHandlers.AddRange(GetHandlersForType(interfaceType));
        }

        var currentType = messageType.BaseType;
        while (currentType != null && currentType != typeof(object))
        {
            allHandlers.AddRange(GetHandlersForType(currentType));
            currentType = currentType.BaseType;
        }

        var handlers = TopologicalSort.Sort(
                allHandlers.Distinct().ToList(),
                h => h.HandlerClassName,
                h => h.OrderBefore,
                h => h.OrderAfter,
                h => h.Order)
            .Select(h => h.PublishAsync)
            .ToArray();

        return _publishCache.GetOrAdd(messageType, handlers);
    }

    private static HandlerRegistration? ConstructClosedRegistration(Type closedMessageType, OpenGenericHandlerDescriptor descriptor)
    {
        try
        {
            var typeArgs = closedMessageType.GetGenericArguments();
            var wrapperClosed = descriptor.WrapperGenericTypeDefinition.MakeGenericType(typeArgs);
            var asyncMethod = wrapperClosed.GetMethod("UntypedHandleAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (asyncMethod == null)
                return null;

            HandleAsyncDelegate asyncDelegate = (mediator, message, ct, returnType) =>
            {
                object? taskObj = asyncMethod.Invoke(null, [mediator, message, ct, returnType]);
                return taskObj is ValueTask<object?> vt ? vt : (ValueTask<object?>)taskObj!;
            };

            HandleDelegate? syncDelegate = null;
            if (!descriptor.IsAsync)
            {
                var syncMethod = wrapperClosed.GetMethod("UntypedHandle", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (syncMethod != null)
                {
                    syncDelegate = (mediator, message, ct, returnType) => syncMethod.Invoke(null, [mediator, message, ct, returnType]);
                }
            }

            return new HandlerRegistration(MessageTypeKey.Get(closedMessageType), wrapperClosed.FullName ?? wrapperClosed.Name, asyncDelegate, syncDelegate, descriptor.IsAsync);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    internal delegate ValueTask InvokeAsyncDelegate(IMediator mediator, object message, CancellationToken cancellationToken);
    internal delegate void InvokeDelegate(IMediator mediator, object message, CancellationToken cancellationToken);
    internal delegate ValueTask<object?> InvokeAsyncResponseDelegate(IMediator mediator, object message, CancellationToken cancellationToken);
    internal delegate object? InvokeResponseDelegate(IMediator mediator, object message, CancellationToken cancellationToken);
}
