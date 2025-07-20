#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class DebugUniqueMessageHandler_Handle_DebugUniqueMessage_StaticWrapper
    {
        public static void Handle(Foundatio.Mediator.Tests.DebugUniqueMessage message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var handlerInstance = GetOrCreateHandler(serviceProvider);
            handlerInstance.Handle(message, serviceProvider.GetRequiredService<Foundatio.Mediator.Tests.DebugTestService>());
        }

        public static object UntypedHandle(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (Foundatio.Mediator.Tests.DebugUniqueMessage)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            Handle(typedMessage, serviceProvider, cancellationToken);
            return new object();
        }

        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "WzC1MqgUwbqMppX7FRYsEE0HAABEZWJ1Z0dlbmVyYXRvclRlc3QuY3M=")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\tests\Foundatio.Mediator.Tests\DebugGeneratorTest.cs(55,18)
        public static void InterceptInvoke0(this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default)
        {
            var typedMessage = (Foundatio.Mediator.Tests.DebugUniqueMessage)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            Handle(typedMessage, serviceProvider, cancellationToken);
        }

        private static Foundatio.Mediator.Tests.DebugUniqueMessageHandler? _handler;
        private static readonly object _lock = new object();

        private static Foundatio.Mediator.Tests.DebugUniqueMessageHandler GetOrCreateHandler(IServiceProvider serviceProvider)
        {
            if (_handler != null)
                return _handler;

            var handlerFromDI = serviceProvider.GetService<Foundatio.Mediator.Tests.DebugUniqueMessageHandler>();
            if (handlerFromDI != null)
                return handlerFromDI;

            lock (_lock)
            {
                if (_handler != null)
                    return _handler;

                _handler = ActivatorUtilities.CreateInstance<Foundatio.Mediator.Tests.DebugUniqueMessageHandler>(serviceProvider);
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
