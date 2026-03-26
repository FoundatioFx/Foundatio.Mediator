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
/// the mediator), this middleware passes through to <c>next()</c> so the full pipeline
/// (logging, validation, auth, etc.) executes before the handler.
/// </para>
/// <para>
/// Order is set low so this middleware runs as the outermost ExecuteAsync wrapper,
/// ensuring fast enqueue with minimal overhead.
/// </para>
/// </remarks>
[Middleware(Order = -100, ExplicitOnly = true)]
public class QueueMiddleware
{
    private static readonly AsyncLocal<bool> s_isProcessing = new();
    private static readonly ConcurrentDictionary<Type, MethodInfo> s_publishMethods = new();

    private static readonly MethodInfo s_publishTypedMethod = typeof(QueueMiddleware)
        .GetMethod(nameof(PublishTypedAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>
    /// Indicates the current async context is processing a message from the bus.
    /// Set by <see cref="MediatorConsumer{T}"/> to prevent re-enqueuing.
    /// </summary>
    internal static bool IsProcessing
    {
        get => s_isProcessing.Value;
        set => s_isProcessing.Value = value;
    }

    private readonly IMessageBus _bus;

    public QueueMiddleware(IMessageBus bus) => _bus = bus;

    public async ValueTask<object?> ExecuteAsync(
        object message,
        HandlerExecutionDelegate next,
        HandlerExecutionInfo handlerInfo)
    {
        // Process path: consumer is calling back through the mediator — run the full pipeline
        if (IsProcessing)
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
