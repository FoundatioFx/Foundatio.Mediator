using Foundatio.Mediator;

namespace Foundatio.Mediator.Queues;

/// <summary>
/// Marks a handler class or method for queue-based processing.
/// When applied, invocations via <c>mediator.InvokeAsync()</c> will publish the message
/// to a message bus for asynchronous processing instead of executing the handler inline.
/// </summary>
/// <remarks>
/// <para>
/// The handler is processed by a <see cref="MediatorConsumer{T}"/> that receives messages
/// from SlimMessageBus and dispatches them back through the mediator pipeline (including
/// all middleware except re-enqueuing).
/// </para>
/// <para>
/// Queue infrastructure is backed by SlimMessageBus, which supports in-memory, Kafka,
/// RabbitMQ, Azure Service Bus, and many other transports.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [Queue(Concurrency = 3)]
/// public class OrderProcessingHandler
/// {
///     public async Task&lt;Result&gt; HandleAsync(
///         ProcessOrder message,
///         CancellationToken ct)
///     {
///         // ... do work ...
///         return Result.Success();
///     }
/// }
/// </code>
/// </example>
[UseMiddleware(typeof(QueueMiddleware))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class QueueAttribute : Attribute
{
    /// <summary>
    /// Override the queue name. Defaults to the message type name.
    /// </summary>
    public string? QueueName { get; set; }

    /// <summary>
    /// Maximum number of retry attempts before dead-lettering. Default is 2 (Foundatio default).
    /// Total attempts = Retries + 1 (initial attempt + retries).
    /// </summary>
    public int Retries { get; set; } = 2;

    /// <summary>
    /// Work item timeout as a TimeSpan string (e.g., "00:05:00").
    /// If a message is not completed within this duration, it is automatically abandoned.
    /// Default is 5 minutes.
    /// </summary>
    public string? Timeout { get; set; }

    /// <summary>
    /// Number of concurrent workers processing this queue. Default is 1.
    /// </summary>
    public int Concurrency { get; set; } = 1;

    /// <summary>
    /// When true, the worker automatically completes the message on success
    /// and abandons it on exception. Default is true.
    /// </summary>
    public bool AutoComplete { get; set; } = true;
}
