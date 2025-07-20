#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class StaticTestHandler_Handle_StaticSyncCommandMessage_StaticWrapper
    {
        public static void Handle(Foundatio.Mediator.Tests.StaticHandlerTest.StaticSyncCommandMessage message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            Foundatio.Mediator.Tests.StaticHandlerTest.StaticTestHandler.Handle(message);
        }

        public static object UntypedHandle(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (Foundatio.Mediator.Tests.StaticHandlerTest.StaticSyncCommandMessage)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            Handle(typedMessage, serviceProvider, cancellationToken);
            return new object();
        }

        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "yM63hrcMQESbVYnBDYBuWtIIAABTdGF0aWNIYW5kbGVyVGVzdC5jcw==")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\tests\Foundatio.Mediator.Tests\StaticHandlerTest.cs(63,18)
        public static void InterceptInvoke0(this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default)
        {
            var typedMessage = (Foundatio.Mediator.Tests.StaticHandlerTest.StaticSyncCommandMessage)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            Handle(typedMessage, serviceProvider, cancellationToken);
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
