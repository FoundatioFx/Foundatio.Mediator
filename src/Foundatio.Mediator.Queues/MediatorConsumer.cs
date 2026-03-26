using SlimMessageBus;

namespace Foundatio.Mediator.Queues;

/// <summary>
/// Generic SlimMessageBus consumer that bridges bus messages back through the mediator pipeline.
/// Sets <see cref="QueueMiddleware.IsProcessing"/> so the middleware passes through
/// to <c>next()</c> instead of re-enqueuing, allowing the full middleware pipeline
/// (logging, validation, auth, etc.) to execute before the handler runs.
/// </summary>
public class MediatorConsumer<T> : IConsumer<T> where T : class
{
    private readonly IMediator _mediator;

    public MediatorConsumer(IMediator mediator) => _mediator = mediator;

    public async Task OnHandle(T message, CancellationToken cancellationToken)
    {
        QueueMiddleware.IsProcessing = true;
        try
        {
            await _mediator.InvokeAsync(message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            QueueMiddleware.IsProcessing = false;
        }
    }
}
