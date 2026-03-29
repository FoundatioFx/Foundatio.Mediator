using System.Collections.Concurrent;
using System.Reflection;
using SlimMessageBus;

namespace Foundatio.Mediator.Queues;

/// <summary>
/// Middleware that intercepts handler invocations for <see cref="QueueAttribute"/>-decorated handlers.
/// </summary>
/// <remarks>
/// <para>
/// On the <b>enqueue path</b> (normal caller), this middleware publishes the message to
/// SlimMessageBus and returns immediately — no other middleware runs.
/// </para>
/// <para>
/// On the <b>process path</b> (when <see cref="MediatorConsumer{T}"/> calls back through
/// the mediator), the presence of a <see cref="QueueContext"/> in <see cref="CallContext.Current"/>
/// signals that this is a processing invocation. The middleware passes through to <c>next()</c>
/// so the full pipeline (logging, validation, auth, etc.) executes before the handler.
/// </para>
/// <para>
/// Order is set low so this middleware runs as the outermost ExecuteAsync wrapper,
/// ensuring fast enqueue with minimal overhead.
/// </para>
/// </remarks>
[Middleware(Order = -100, ExplicitOnly = true)]
public class QueueMiddleware
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> s_publishMethods = new();

    private static readonly MethodInfo s_publishTypedMethod = typeof(QueueMiddleware)
        .GetMethod(nameof(PublishTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly IMessageBus _bus;

    public QueueMiddleware(IMessageBus bus) => _bus = bus;

    public async ValueTask<object?> ExecuteAsync(
        object message,
        HandlerExecutionDelegate next,
        HandlerExecutionInfo handlerInfo,
        CallContext? callContext)
    {
        // Process path: QueueContext in CallContext signals we're processing from the bus
        if (callContext?.TryGet<QueueContext>(out _) == true)
            return await next().ConfigureAwait(false);

        // Enqueue path: publish to the bus and return immediately
        var method = s_publishMethods.GetOrAdd(message.GetType(),
            type => s_publishTypedMethod.MakeGenericMethod(type));

        await ((Task)method.Invoke(null, [_bus, message])!).ConfigureAwait(false);

        return Result.Accepted("Message queued");
    }

    private static Task PublishTypedAsync<T>(IMessageBus bus, T message) where T : class
        => bus.Publish(message);
}
