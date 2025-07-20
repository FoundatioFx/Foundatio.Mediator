using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal class OrderAuditHandler_HandleAsync_Wrapper : IHandler<ConsoleSample.Messages.OrderCreatedEvent>
    {
        private readonly IServiceProvider _serviceProvider;

        public OrderAuditHandler_HandleAsync_Wrapper(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async ValueTask<TResponse> HandleAsync<TResponse>(ConsoleSample.Messages.OrderCreatedEvent message, CancellationToken cancellationToken)
        {
            var handler = _serviceProvider.GetRequiredService<ConsoleSample.Handlers.OrderAuditHandler>();
            await handler.HandleAsync(message, _serviceProvider.GetRequiredService<ConsoleSample.Services.IAuditService>(), cancellationToken);
            return default(TResponse)!;
        }
    }
}
