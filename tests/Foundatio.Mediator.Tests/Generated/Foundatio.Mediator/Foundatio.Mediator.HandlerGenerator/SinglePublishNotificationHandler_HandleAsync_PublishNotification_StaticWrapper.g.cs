#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class SinglePublishNotificationHandler_HandleAsync_PublishNotification_StaticWrapper
    {
        public static async System.Threading.Tasks.Task HandleAsync(Foundatio.Mediator.Tests.PublishNotification message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var handlerInstance = GetOrCreateHandler(serviceProvider);
            await handlerInstance.HandleAsync(message, serviceProvider.GetRequiredService<Foundatio.Mediator.Tests.SingleHandlerTestService>(), cancellationToken);
        }

        public static async ValueTask<object> UntypedHandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (Foundatio.Mediator.Tests.PublishNotification)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            await HandleAsync(typedMessage, serviceProvider, cancellationToken);
            return new object();
        }

        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "Y0+ZEg1jqrnbhnUhOSPdkU0JAABQdWJsaXNoVGVzdHMuY3M=")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\tests\Foundatio.Mediator.Tests\PublishTests.cs(59,24)
        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "Y0+ZEg1jqrnbhnUhOSPdkVIfAABQdWJsaXNoVGVzdHMuY3M=")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\tests\Foundatio.Mediator.Tests\PublishTests.cs(182,28)
        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "3ASTdmIFACpnqPzHMLJ6mYUEAABTaW5nbGVIYW5kbGVyVGVzdC5jcw==")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\tests\Foundatio.Mediator.Tests\SingleHandlerTest.cs(33,24)
        public static async global::System.Threading.Tasks.ValueTask InterceptInvokeAsync0(this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default)
        {
            var typedMessage = (Foundatio.Mediator.Tests.PublishNotification)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            await HandleAsync(typedMessage, serviceProvider, cancellationToken);
        }

        private static Foundatio.Mediator.Tests.SinglePublishNotificationHandler? _handler;
        private static readonly object _lock = new object();

        private static Foundatio.Mediator.Tests.SinglePublishNotificationHandler GetOrCreateHandler(IServiceProvider serviceProvider)
        {
            if (_handler != null)
                return _handler;

            var handlerFromDI = serviceProvider.GetService<Foundatio.Mediator.Tests.SinglePublishNotificationHandler>();
            if (handlerFromDI != null)
                return handlerFromDI;

            lock (_lock)
            {
                if (_handler != null)
                    return _handler;

                _handler = ActivatorUtilities.CreateInstance<Foundatio.Mediator.Tests.SinglePublishNotificationHandler>(serviceProvider);
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
