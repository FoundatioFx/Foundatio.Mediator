#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class StaticTestHandler_Handle_StaticQueryWithDIMessage_StaticWrapper
    {
        public static string Handle(Foundatio.Mediator.Tests.StaticHandlerTest.StaticQueryWithDIMessage message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            return Foundatio.Mediator.Tests.StaticHandlerTest.StaticTestHandler.Handle(message, serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Foundatio.Mediator.Tests.StaticHandlerTest.StaticTestHandler>>());
        }

        public static object UntypedHandle(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (Foundatio.Mediator.Tests.StaticHandlerTest.StaticQueryWithDIMessage)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            var result = Handle(typedMessage, serviceProvider, cancellationToken);
            return result ?? new object();
        }

        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "yM63hrcMQESbVYnBDYBuWvEQAABTdGF0aWNIYW5kbGVyVGVzdC5jcw==")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\tests\Foundatio.Mediator.Tests\StaticHandlerTest.cs(121,31)
        public static string InterceptInvoke0(this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default)
        {
            var typedMessage = (Foundatio.Mediator.Tests.StaticHandlerTest.StaticQueryWithDIMessage)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            return Handle(typedMessage, serviceProvider, cancellationToken);
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
