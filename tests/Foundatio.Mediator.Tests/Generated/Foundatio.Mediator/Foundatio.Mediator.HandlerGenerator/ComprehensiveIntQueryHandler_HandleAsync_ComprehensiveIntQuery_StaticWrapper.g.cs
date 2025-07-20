#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class ComprehensiveIntQueryHandler_HandleAsync_ComprehensiveIntQuery_StaticWrapper
    {
        public static async System.Threading.Tasks.Task<int> HandleAsync(Foundatio.Mediator.Tests.ComprehensiveIntQuery message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var handlerInstance = GetOrCreateHandler(serviceProvider);
            return await handlerInstance.HandleAsync(message, cancellationToken);
        }

        public static async ValueTask<object> UntypedHandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (Foundatio.Mediator.Tests.ComprehensiveIntQuery)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            var result = await HandleAsync(typedMessage, serviceProvider, cancellationToken);
            return result;
        }

        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "8vx6ML42qwG8QBnCWP4Sy4UJAABIYW5kbGVyV3JhcHBlckNvbXByZWhlbnNpdmVUZXN0LmNz")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\tests\Foundatio.Mediator.Tests\HandlerWrapperComprehensiveTest.cs(53,40)
        public static async global::System.Threading.Tasks.ValueTask<int> InterceptInvokeAsync0(this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default)
        {
            var typedMessage = (Foundatio.Mediator.Tests.ComprehensiveIntQuery)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            return await HandleAsync(typedMessage, serviceProvider, cancellationToken);
        }

        private static Foundatio.Mediator.Tests.ComprehensiveIntQueryHandler? _handler;
        private static readonly object _lock = new object();

        private static Foundatio.Mediator.Tests.ComprehensiveIntQueryHandler GetOrCreateHandler(IServiceProvider serviceProvider)
        {
            if (_handler != null)
                return _handler;

            var handlerFromDI = serviceProvider.GetService<Foundatio.Mediator.Tests.ComprehensiveIntQueryHandler>();
            if (handlerFromDI != null)
                return handlerFromDI;

            lock (_lock)
            {
                if (_handler != null)
                    return _handler;

                _handler = ActivatorUtilities.CreateInstance<Foundatio.Mediator.Tests.ComprehensiveIntQueryHandler>(serviceProvider);
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
