using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal class CreatePersonalizedGreetingHandler_HandleAsync_Wrapper : IHandler<ConsoleSample.Messages.CreatePersonalizedGreetingQuery>
    {
        private readonly IServiceProvider _serviceProvider;

        public CreatePersonalizedGreetingHandler_HandleAsync_Wrapper(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async ValueTask<TResponse> HandleAsync<TResponse>(ConsoleSample.Messages.CreatePersonalizedGreetingQuery message, CancellationToken cancellationToken)
        {
            var handler = _serviceProvider.GetRequiredService<ConsoleSample.Handlers.CreatePersonalizedGreetingHandler>();
            var result = await handler.HandleAsync(message, _serviceProvider.GetRequiredService<ConsoleSample.Services.IGreetingService>(), _serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ConsoleSample.Handlers.CreatePersonalizedGreetingHandler>>(), cancellationToken);
            return (TResponse)(object)result!;
        }
    }
}
