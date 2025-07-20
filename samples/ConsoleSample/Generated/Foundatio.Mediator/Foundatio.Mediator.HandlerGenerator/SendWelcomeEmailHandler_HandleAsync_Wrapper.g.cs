using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal class SendWelcomeEmailHandler_HandleAsync_Wrapper : IHandler<ConsoleSample.Messages.SendWelcomeEmailCommand>
    {
        private readonly IServiceProvider _serviceProvider;

        public SendWelcomeEmailHandler_HandleAsync_Wrapper(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async ValueTask<TResponse> HandleAsync<TResponse>(ConsoleSample.Messages.SendWelcomeEmailCommand message, CancellationToken cancellationToken)
        {
            var handler = _serviceProvider.GetRequiredService<ConsoleSample.Handlers.SendWelcomeEmailHandler>();
            await handler.HandleAsync(message, _serviceProvider.GetRequiredService<ConsoleSample.Services.IEmailService>(), _serviceProvider.GetRequiredService<ConsoleSample.Services.IGreetingService>(), cancellationToken);
            return default(TResponse)!;
        }
    }
}
