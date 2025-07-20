#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class FoundatioUserRegisteredWelcomeHandler_HandleAsync_UserRegisteredEvent_StaticWrapper
    {
        public static async System.Threading.Tasks.Task HandleAsync(Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var handlerInstance = GetOrCreateHandler(serviceProvider);
            await handlerInstance.HandleAsync(message, cancellationToken);
        }

        public static async ValueTask<object> UntypedHandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            await HandleAsync(typedMessage, serviceProvider, cancellationToken);
            return new object();
        }

        private static Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioUserRegisteredWelcomeHandler? _handler;
        private static readonly object _lock = new object();

        private static Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioUserRegisteredWelcomeHandler GetOrCreateHandler(IServiceProvider serviceProvider)
        {
            if (_handler != null)
                return _handler;

            var handlerFromDI = serviceProvider.GetService<Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioUserRegisteredWelcomeHandler>();
            if (handlerFromDI != null)
                return handlerFromDI;

            lock (_lock)
            {
                if (_handler != null)
                    return _handler;

                _handler = ActivatorUtilities.CreateInstance<Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioUserRegisteredWelcomeHandler>(serviceProvider);
                return _handler;
            }
        }
    }
}
