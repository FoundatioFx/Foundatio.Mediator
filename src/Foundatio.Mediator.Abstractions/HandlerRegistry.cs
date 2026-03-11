using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundatio.Mediator;

/// <summary>
/// Central registry for all handler registrations. Populated at startup during
/// <c>AddMediator</c> and frozen (made immutable) before the application runs.
/// </summary>
public sealed class HandlerRegistry : IDisposable
{
    private readonly Dictionary<string, List<HandlerRegistration>> _handlersByMessageType = new();
    private readonly List<OpenGenericHandlerDescriptor> _openGenericDescriptors = new();
    private readonly List<HandlerRegistration> _allRegistrations = new();
    private readonly List<MiddlewareRegistration> _allMiddleware = new();
    private volatile bool _frozen;

    private readonly ConcurrentDictionary<Type, InvokeAsyncDelegate> _invokeAsyncCache = new();
    private readonly ConcurrentDictionary<Type, InvokeDelegate> _invokeCache = new();
    private readonly ConcurrentDictionary<(Type MessageType, Type ResponseType), InvokeAsyncResponseDelegate> _invokeAsyncWithResponseCache = new();
    private readonly ConcurrentDictionary<(Type MessageType, Type ResponseType), InvokeResponseDelegate> _invokeWithResponseCache = new();
    private readonly ConcurrentDictionary<Type, PublishAsyncDelegate[]> _publishCache = new();
    private readonly ConcurrentDictionary<Type, HandlerRegistration?> _openGenericClosedCache = new();

    // Subscription type → array of entries (copy-on-write per group).
    private volatile Dictionary<Type, SubscriptionEntry[]> _subscriptionGroups = new();
    // Message type → matching subscription types (invalidated when subscription types change).
    private readonly ConcurrentDictionary<Type, Type[]> _messageTypeMatchCache = new();
    private readonly object _subscriptionWriteLock = new();
    private volatile bool _disposed;

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
    /// Adds a middleware registration to the registry. Must be called before <see cref="Freeze"/>.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void AddMiddleware(MiddlewareRegistration registration)
    {
        if (_frozen) throw new InvalidOperationException("Cannot add middleware after the registry has been frozen.");

        _allMiddleware.Add(registration);
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
    /// Gets all middleware registrations in the registry.
    /// </summary>
    public IReadOnlyList<MiddlewareRegistration> MiddlewareRegistrations => _allMiddleware;

    /// <summary>
    /// Logs all registered handlers in an aligned, diagnostic-friendly format.
    /// </summary>
    /// <param name="logger">The logger to write to. When null, writes to the console.</param>
    public void ShowRegisteredHandlers(ILogger? logger = null)
    {
        Action<string> writeLog = logger != null
            ? msg => logger.LogInformation("{MediatorHandlerInfo}", msg)
            : Console.WriteLine;

        if (_allRegistrations.Count == 0)
        {
            writeLog("Foundatio.Mediator: no handlers registered.");
            return;
        }

        var sorted = _allRegistrations
            .OrderBy(r => r.MessageTypeName)
            .ThenBy(r => r.HandlerClassName)
            .ToList();

        // Compute column widths for aligned output
        int maxMsg = sorted.Max(r => GetShortTypeName(r.MessageTypeName).Length);
        int maxHandler = sorted.Max(r => FormatHandlerColumn(r).Length);
        int maxReturn = sorted.Max(r => (r.ReturnTypeName ?? "").Length);

        writeLog($"Foundatio.Mediator registered {sorted.Count} handler(s):");

        foreach (var r in sorted)
        {
            var msg = GetShortTypeName(r.MessageTypeName).PadRight(maxMsg);
            var handler = FormatHandlerColumn(r).PadRight(maxHandler);
            var ret = (r.ReturnTypeName ?? "").PadRight(maxReturn);
            var flags = r.IsAsync ? "  [async]" : "";
            writeLog($"  {msg}  \u2192 {handler}  {ret}{flags}");
        }
    }

    private static string FormatHandlerColumn(HandlerRegistration r)
    {
        if (r.SourceHandlerName != null && r.MethodName != null)
            return $"{r.SourceHandlerName}.{r.MethodName}";

        return r.HandlerClassName;
    }

    private static string GetShortTypeName(string fullyQualifiedName)
    {
        var idx = fullyQualifiedName.LastIndexOf('.');
        return idx >= 0 ? fullyQualifiedName.Substring(idx + 1) : fullyQualifiedName;
    }

    /// <summary>
    /// Logs the middleware pipeline in an aligned, diagnostic-friendly format.
    /// </summary>
    /// <param name="logger">The logger to write to. When null, writes to the console.</param>
    public void ShowRegisteredMiddleware(ILogger? logger = null)
    {
        Action<string> writeLog = logger != null
            ? msg => logger.LogInformation("{MediatorMiddlewareInfo}", msg)
            : Console.WriteLine;

        if (_allMiddleware.Count == 0)
        {
            writeLog("Foundatio.Mediator: no middleware registered.");
            return;
        }

        // Sort by order (nulls last), then name
        var sorted = _allMiddleware
            .OrderBy(m => m.Order ?? int.MaxValue)
            .ThenBy(m => m.Name)
            .ToList();

        int maxName = sorted.Max(m => m.Name.Length);
        int maxHooks = sorted.Max(m => m.Hooks.Length);
        int maxScope = sorted.Max(m => m.MessageScope.Length);

        writeLog($"Foundatio.Mediator middleware pipeline ({sorted.Count}):");

        for (int i = 0; i < sorted.Count; i++)
        {
            var m = sorted[i];
            var num = $"{i + 1}.".PadRight(4);
            var name = m.Name.PadRight(maxName);
            var hooks = $"[{m.Hooks}]".PadRight(maxHooks + 2);
            var scope = m.MessageScope == "object" ? "" : $"  <{m.MessageScope}>";
            var orderStr = m.Order.HasValue ? $"  Order: {m.Order}" : "";
            var flags = new List<string>();
            if (m.IsStatic) flags.Add("static");
            if (m.IsExplicitOnly) flags.Add("explicit-only");
            var flagStr = flags.Count > 0 ? $"  [{string.Join(", ", flags)}]" : "";
            writeLog($"  {num}{name}  {hooks}{orderStr}{scope}{flagStr}");
        }
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
            return (mediator, msg, ct) => DiscardResult(handler.HandleAsync(mediator, msg, ct, null));
        });
    }

    /// <summary>
    /// Converts a <see cref="ValueTask{T}"/> to a non-generic <see cref="ValueTask"/>,
    /// avoiding an async state machine allocation on the synchronous completion hot path.
    /// </summary>
    [DebuggerStepThrough]
    private static ValueTask DiscardResult(ValueTask<object?> task)
    {
        if (task.IsCompletedSuccessfully)
        {
            _ = task.Result;
            return default;
        }

        return Awaited(task);
        static async ValueTask Awaited(ValueTask<object?> t) { _ = await t; }
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

    #region Dynamic Subscriptions

    /// <summary>
    /// Returns <c>true</c> when at least one dynamic subscriber is active.
    /// </summary>
    public bool HasSubscribers
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _subscriptionGroups.Count > 0;
    }

    /// <summary>
    /// Creates a dynamic subscription that yields published notifications assignable to
    /// <typeparamref name="T"/>. The stream ends when <paramref name="cancellationToken"/>
    /// is cancelled (e.g., when the SSE client disconnects).
    /// </summary>
    /// <typeparam name="T">
    /// The notification type to subscribe to. Can be a concrete type, base class, or interface.
    /// Messages are matched using <see cref="Type.IsAssignableFrom"/>.
    /// </typeparam>
    /// <param name="cancellationToken">Token that ends the subscription when cancelled.</param>
    /// <param name="options">Optional settings controlling buffer capacity and other subscription behavior.</param>
    /// <returns>An async stream of matching notifications.</returns>
    public async IAsyncEnumerable<T> SubscribeAsync<T>(
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        SubscriberOptions? options = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HandlerRegistry));

        var maxCapacity = options?.MaxCapacity ?? 100;
        var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(maxCapacity)
        {
            FullMode = options?.FullMode ?? BoundedChannelFullMode.DropOldest,
            SingleWriter = false,
            SingleReader = true
        });

        var entry = new SubscriptionEntry(
            msg => channel.Writer.TryWrite((T)msg),
            () => channel.Writer.TryComplete());

        AddSubscription(typeof(T), entry);
        try
        {
            // Use WaitToReadAsync + TryRead instead of ReadAllAsync so we can
            // catch OperationCanceledException without wrapping yield return in
            // a try/catch block (which C# disallows).
            while (true)
            {
                bool canRead;
                try
                {
                    canRead = await channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (!canRead)
                    break;

                while (channel.Reader.TryRead(out var item))
                {
                    yield return item;
                }
            }
        }
        finally
        {
            RemoveSubscription(typeof(T), entry);
            channel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Fans out a published message to all active dynamic subscribers whose type filter matches.
    /// Non-blocking: never awaits.
    /// </summary>
    /// <param name="message">The notification that was just published.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TryWriteSubscription(object message)
    {
        // Volatile read — gets a consistent snapshot (copy-on-write).
        var groups = _subscriptionGroups;
        if (groups.Count == 0)
            return;

        var messageType = message.GetType();

        // One-time IsAssignableFrom check per unique message type; cached thereafter.
        var matchingTypes = _messageTypeMatchCache.GetOrAdd(messageType, mt =>
        {
            var g = _subscriptionGroups; // Volatile read for the latest subscription types.
            var matches = new List<Type>();
            foreach (var subscriptionType in g.Keys)
            {
                if (subscriptionType.IsAssignableFrom(mt))
                    matches.Add(subscriptionType);
            }
            return matches.ToArray();
        });

        for (int i = 0; i < matchingTypes.Length; i++)
        {
            if (groups.TryGetValue(matchingTypes[i], out var entries))
            {
                for (int j = 0; j < entries.Length; j++)
                    entries[j].Write(message);
            }
        }
    }

    private void AddSubscription(Type subscriptionType, SubscriptionEntry entry)
    {
        lock (_subscriptionWriteLock)
        {
            var current = _subscriptionGroups;
            var next = new Dictionary<Type, SubscriptionEntry[]>(current);

            if (next.TryGetValue(subscriptionType, out var existing))
            {
                var arr = new SubscriptionEntry[existing.Length + 1];
                existing.CopyTo(arr, 0);
                arr[existing.Length] = entry;
                next[subscriptionType] = arr;
            }
            else
            {
                next[subscriptionType] = [entry];
                // New subscription type — invalidate the message-type match cache.
                _messageTypeMatchCache.Clear();
            }

            _subscriptionGroups = next; // Volatile write.
        }
    }

    private void RemoveSubscription(Type subscriptionType, SubscriptionEntry entry)
    {
        lock (_subscriptionWriteLock)
        {
            var current = _subscriptionGroups;
            if (!current.TryGetValue(subscriptionType, out var existing))
                return;

            int index = Array.IndexOf(existing, entry);
            if (index < 0)
                return;

            var next = new Dictionary<Type, SubscriptionEntry[]>(current);

            if (existing.Length == 1)
            {
                next.Remove(subscriptionType);
                // Subscription type removed — invalidate the message-type match cache.
                _messageTypeMatchCache.Clear();
            }
            else
            {
                var arr = new SubscriptionEntry[existing.Length - 1];
                Array.Copy(existing, 0, arr, 0, index);
                Array.Copy(existing, index + 1, arr, index, existing.Length - index - 1);
                next[subscriptionType] = arr;
            }

            _subscriptionGroups = next; // Volatile write.
        }
    }

    private sealed class SubscriptionEntry(Action<object> write, Action complete)
    {
        public void Write(object message) => write(message);
        public void Complete() => complete();
    }

    #endregion

    /// <summary>
    /// Completes all active subscription channels so that <see cref="SubscribeAsync{T}"/>
    /// consumers unblock and exit cleanly.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        SubscriptionEntry[][] groupsToComplete;
        lock (_subscriptionWriteLock)
        {
            if (_disposed)
                return;
            _disposed = true;

            var groups = _subscriptionGroups;
            groupsToComplete = new SubscriptionEntry[groups.Count][];
            groups.Values.CopyTo(groupsToComplete, 0);

            _subscriptionGroups = new Dictionary<Type, SubscriptionEntry[]>();
            _messageTypeMatchCache.Clear();
        }

        // Complete all channels outside the lock so SubscribeAsync consumers can unblock.
        for (int i = 0; i < groupsToComplete.Length; i++)
        {
            var entries = groupsToComplete[i];
            for (int j = 0; j < entries.Length; j++)
                entries[j].Complete();
        }
    }

    internal delegate ValueTask InvokeAsyncDelegate(IMediator mediator, object message, CancellationToken cancellationToken);
    internal delegate void InvokeDelegate(IMediator mediator, object message, CancellationToken cancellationToken);
    internal delegate ValueTask<object?> InvokeAsyncResponseDelegate(IMediator mediator, object message, CancellationToken cancellationToken);
    internal delegate object? InvokeResponseDelegate(IMediator mediator, object message, CancellationToken cancellationToken);
}
