using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal class GreetingHandler_Handle_Wrapper : IHandler<ConsoleSample.Messages.GreetingQuery>
    {
        private readonly IServiceProvider _serviceProvider;

        public GreetingHandler_Handle_Wrapper(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async ValueTask<TResponse> HandleAsync<TResponse>(ConsoleSample.Messages.GreetingQuery message, CancellationToken cancellationToken)
        {
            var handler = _serviceProvider.GetRequiredService<ConsoleSample.Handlers.GreetingHandler>();
            var result = handler.Handle(message);
            return (TResponse)(object)result!;
        }
    }
}
