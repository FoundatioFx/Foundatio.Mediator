using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal class PingHandler_HandleAsync_Wrapper : IHandler<ConsoleSample.Messages.PingCommand>
    {
        private readonly IServiceProvider _serviceProvider;

        public PingHandler_HandleAsync_Wrapper(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async ValueTask<TResponse> HandleAsync<TResponse>(ConsoleSample.Messages.PingCommand message, CancellationToken cancellationToken)
        {
            var handler = _serviceProvider.GetRequiredService<ConsoleSample.Handlers.PingHandler>();
            await handler.HandleAsync(message, cancellationToken);
            return default(TResponse)!;
        }
    }
}
