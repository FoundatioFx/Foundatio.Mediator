#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class StaticTestHandler_HandleAsync_StaticAsyncQueryMessage_StaticWrapper
    {
        public static async System.Threading.Tasks.Task<string> HandleAsync(Foundatio.Mediator.Tests.StaticHandlerTest.StaticAsyncQueryMessage message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            return await Foundatio.Mediator.Tests.StaticHandlerTest.StaticTestHandler.HandleAsync(message, cancellationToken);
        }

        public static async ValueTask<object> UntypedHandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (Foundatio.Mediator.Tests.StaticHandlerTest.StaticAsyncQueryMessage)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            var result = await HandleAsync(typedMessage, serviceProvider, cancellationToken);
            return result ?? new object();
        }

        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "yM63hrcMQESbVYnBDYBuWtoOAABTdGF0aWNIYW5kbGVyVGVzdC5jcw==")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\tests\Foundatio.Mediator.Tests\StaticHandlerTest.cs(106,37)
        public static async global::System.Threading.Tasks.ValueTask<string> InterceptInvokeAsync0(this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default)
        {
            var typedMessage = (Foundatio.Mediator.Tests.StaticHandlerTest.StaticAsyncQueryMessage)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            return await HandleAsync(typedMessage, serviceProvider, cancellationToken);
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
