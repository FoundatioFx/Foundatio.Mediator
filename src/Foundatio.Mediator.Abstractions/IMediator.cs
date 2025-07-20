using System;
using System.Threading;
using System.Threading.Tasks;

namespace Foundatio.Mediator
{
    public interface IMediator
    {
        ValueTask InvokeAsync(object message, CancellationToken cancellationToken = default);
        ValueTask<TResponse> InvokeAsync<TResponse>(object message, CancellationToken cancellationToken = default);
        void Invoke(object message, CancellationToken cancellationToken = default);
        TResponse Invoke<TResponse>(object message, CancellationToken cancellationToken = default);
        
        ValueTask PublishAsync(object message, CancellationToken cancellationToken = default);
        void Publish(object message, CancellationToken cancellationToken = default);
    }
}
