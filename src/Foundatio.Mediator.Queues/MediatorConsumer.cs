using SlimMessageBus;

namespace Foundatio.Mediator.Queues;

/// <summary>
/// Generic SlimMessageBus consumer that bridges bus messages back through the mediator pipeline.
/// Calls the handler's <see cref="HandleAsyncDelegate"/> directly with a <see cref="CallContext"/>
/// containing a <see cref="QueueContext"/>, so the <see cref="QueueMiddleware"/> passes through
/// to <c>next()</c> instead of re-enqueuing, and so that handler methods can inject
/// <see cref="QueueContext"/> for progress reporting and timeout renewal.
/// </summary>
public class MediatorConsumer<T> : IConsumer<T> where T : class
{
    private readonly IMediator _mediator;
    private readonly HandlerRegistration _registration;
    private readonly string _queueName;

    public MediatorConsumer(IMediator mediator, HandlerRegistry registry)
    {
        _mediator = mediator;

        var registrations = registry.GetRegistrationsForMessageType(typeof(T));
        _registration = registrations.Count switch
        {
            0 => throw new InvalidOperationException($"No handler registration found for message type {typeof(T).Name}"),
            1 => registrations[0],
            _ => throw new InvalidOperationException($"Multiple handler registrations found for message type {typeof(T).Name}. Queue messages must have exactly one handler.")
        };

        var queueAttr = _registration.GetPreferredAttribute<QueueAttribute>()?.Attribute as QueueAttribute;
        _queueName = !string.IsNullOrWhiteSpace(queueAttr?.QueueName)
            ? queueAttr!.QueueName!
            : typeof(T).Name;
    }

    public async Task OnHandle(T message, CancellationToken cancellationToken)
    {
        var queueContext = new QueueContext
        {
            QueueName = _queueName,
            MessageType = typeof(T)
        };

        using var callContext = CallContext.Rent().Set(queueContext);
        await _registration.HandleAsync(_mediator, message, callContext, cancellationToken, null).ConfigureAwait(false);
    }
}
