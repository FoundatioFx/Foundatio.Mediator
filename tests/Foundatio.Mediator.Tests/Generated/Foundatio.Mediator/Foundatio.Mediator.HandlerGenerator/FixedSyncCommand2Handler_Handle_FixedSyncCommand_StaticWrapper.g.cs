#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class FixedSyncCommand2Handler_Handle_FixedSyncCommand_StaticWrapper
    {
        public static void Handle(Foundatio.Mediator.Tests.FixedSyncCommand message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var handlerInstance = GetOrCreateHandler(serviceProvider);
            handlerInstance.Handle(message, serviceProvider.GetRequiredService<Foundatio.Mediator.Tests.FixedTestService>());
        }

        public static object UntypedHandle(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (Foundatio.Mediator.Tests.FixedSyncCommand)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            Handle(typedMessage, serviceProvider, cancellationToken);
            return new object();
        }

        private static Foundatio.Mediator.Tests.FixedSyncCommand2Handler? _handler;
        private static readonly object _lock = new object();

        private static Foundatio.Mediator.Tests.FixedSyncCommand2Handler GetOrCreateHandler(IServiceProvider serviceProvider)
        {
            if (_handler != null)
                return _handler;

            var handlerFromDI = serviceProvider.GetService<Foundatio.Mediator.Tests.FixedSyncCommand2Handler>();
            if (handlerFromDI != null)
                return handlerFromDI;

            lock (_lock)
            {
                if (_handler != null)
                    return _handler;

                _handler = ActivatorUtilities.CreateInstance<Foundatio.Mediator.Tests.FixedSyncCommand2Handler>(serviceProvider);
                return _handler;
            }
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<System.Type, object> _middlewareCache = new();

        private static T GetOrCreateMiddleware<T>(IServiceProvider serviceProvider) where T : class
        {
            // Check cache first - if it's there, it means it's not registered in DI
            if (_middlewareCache.TryGetValue(typeof(T), out var cachedInstance))
                return (T)cachedInstance;

            // Try to get from DI - if registered, always use DI (respects service lifetime)
            var middlewareFromDI = serviceProvider.GetService<T>();
            if (middlewareFromDI != null)
                return middlewareFromDI;

            // Not in DI, create and cache our own instance
            return (T)_middlewareCache.GetOrAdd(typeof(T), type => 
                ActivatorUtilities.CreateInstance<T>(serviceProvider));
        }
    }
}
