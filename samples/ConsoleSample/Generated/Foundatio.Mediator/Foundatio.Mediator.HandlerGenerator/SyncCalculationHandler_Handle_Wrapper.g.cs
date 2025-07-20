using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal class SyncCalculationHandler_Handle_Wrapper : IHandler<ConsoleSample.Messages.SyncCalculationQuery>
    {
        private readonly IServiceProvider _serviceProvider;

        public SyncCalculationHandler_Handle_Wrapper(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async ValueTask<TResponse> HandleAsync<TResponse>(ConsoleSample.Messages.SyncCalculationQuery message, CancellationToken cancellationToken)
        {
            var handler = _serviceProvider.GetRequiredService<ConsoleSample.Handlers.SyncCalculationHandler>();
            var result = handler.Handle(message);
            return (TResponse)(object)result!;
        }
    }
}
