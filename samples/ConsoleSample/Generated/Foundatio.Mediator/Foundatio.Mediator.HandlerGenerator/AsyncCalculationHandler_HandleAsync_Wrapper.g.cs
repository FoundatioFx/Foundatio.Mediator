using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal class AsyncCalculationHandler_HandleAsync_Wrapper : IHandler<ConsoleSample.Messages.AsyncCalculationQuery>
    {
        private readonly IServiceProvider _serviceProvider;

        public AsyncCalculationHandler_HandleAsync_Wrapper(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async ValueTask<TResponse> HandleAsync<TResponse>(ConsoleSample.Messages.AsyncCalculationQuery message, CancellationToken cancellationToken)
        {
            var handler = _serviceProvider.GetRequiredService<ConsoleSample.Handlers.AsyncCalculationHandler>();
            var result = await handler.HandleAsync(message, cancellationToken);
            return (TResponse)(object)result!;
        }
    }
}
