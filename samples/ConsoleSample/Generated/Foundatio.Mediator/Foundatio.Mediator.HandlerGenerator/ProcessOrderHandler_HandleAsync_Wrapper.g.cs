using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal class ProcessOrderHandler_HandleAsync_Wrapper : IHandler<ConsoleSample.Messages.ProcessOrderCommand>
    {
        private readonly IServiceProvider _serviceProvider;

        public ProcessOrderHandler_HandleAsync_Wrapper(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async ValueTask<TResponse> HandleAsync<TResponse>(ConsoleSample.Messages.ProcessOrderCommand message, CancellationToken cancellationToken)
        {
            var handler = _serviceProvider.GetRequiredService<ConsoleSample.Handlers.ProcessOrderHandler>();
            var result = await handler.HandleAsync(message, cancellationToken);
            return (TResponse)(object)result!;
        }
    }
}
