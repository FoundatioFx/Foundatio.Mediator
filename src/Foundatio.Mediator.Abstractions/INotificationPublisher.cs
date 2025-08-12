namespace Foundatio.Mediator;

public delegate ValueTask PublishAsyncDelegate(IMediator mediator, object message, CancellationToken cancellationToken);

public interface INotificationPublisher
{
    ValueTask PublishAsync(IMediator mediator, IEnumerable<PublishAsyncDelegate> handlers, object message, CancellationToken cancellationToken);
}

public class ForeachAwaitPublisher : INotificationPublisher
{
    public async ValueTask PublishAsync(IMediator mediator, IEnumerable<PublishAsyncDelegate> handlers, object message, CancellationToken cancellationToken)
    {
        foreach (var handler in handlers)
        {
            await handler(mediator, message, cancellationToken).ConfigureAwait(false);
        }
    }
}

public class TaskWhenAllPublisher : INotificationPublisher
{
    public async ValueTask PublishAsync(IMediator mediator, IEnumerable<PublishAsyncDelegate> handlers, object message, CancellationToken cancellationToken)
    {
        var tasks = handlers.Select(h => h(mediator, message, cancellationToken));
        await Task.WhenAll(tasks.Select(t => t.AsTask()));
    }
}
