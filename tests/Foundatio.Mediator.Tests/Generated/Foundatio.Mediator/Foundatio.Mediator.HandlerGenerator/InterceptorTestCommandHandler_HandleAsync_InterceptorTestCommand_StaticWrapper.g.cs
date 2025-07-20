#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class InterceptorTestCommandHandler_HandleAsync_InterceptorTestCommand_StaticWrapper
    {
        public static async System.Threading.Tasks.Task<string> HandleAsync(Foundatio.Mediator.Tests.InterceptorTestCommand message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var handlerInstance = GetOrCreateHandler(serviceProvider);
            return await handlerInstance.HandleAsync(message, cancellationToken);
        }

        public static async ValueTask<object> UntypedHandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (Foundatio.Mediator.Tests.InterceptorTestCommand)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            var result = await HandleAsync(typedMessage, serviceProvider, cancellationToken);
            return result ?? new object();
        }

        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "R5SA+bfDgSrzRLS491ROCrwEAABJbnRlcmNlcHRvckZ1bmN0aW9uVGVzdC5jcw==")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\tests\Foundatio.Mediator.Tests\InterceptorFunctionTest.cs(34,37)
        public static async global::System.Threading.Tasks.ValueTask<string> InterceptInvokeAsync0(this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default)
        {
            var typedMessage = (Foundatio.Mediator.Tests.InterceptorTestCommand)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            return await HandleAsync(typedMessage, serviceProvider, cancellationToken);
        }

        private static Foundatio.Mediator.Tests.InterceptorTestCommandHandler? _handler;
        private static readonly object _lock = new object();

        private static Foundatio.Mediator.Tests.InterceptorTestCommandHandler GetOrCreateHandler(IServiceProvider serviceProvider)
        {
            if (_handler != null)
                return _handler;

            var handlerFromDI = serviceProvider.GetService<Foundatio.Mediator.Tests.InterceptorTestCommandHandler>();
            if (handlerFromDI != null)
                return handlerFromDI;

            lock (_lock)
            {
                if (_handler != null)
                    return _handler;

                _handler = ActivatorUtilities.CreateInstance<Foundatio.Mediator.Tests.InterceptorTestCommandHandler>(serviceProvider);
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
