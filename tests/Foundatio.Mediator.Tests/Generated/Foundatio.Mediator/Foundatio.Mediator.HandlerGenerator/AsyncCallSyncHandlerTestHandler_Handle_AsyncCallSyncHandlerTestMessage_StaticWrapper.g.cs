#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class AsyncCallSyncHandlerTestHandler_Handle_AsyncCallSyncHandlerTestMessage_StaticWrapper
    {
        public static async System.Threading.Tasks.Task<string> Handle(Foundatio.Mediator.Tests.AsyncCallSyncHandlerTestMessage message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var middlewareInstance = GetOrCreateMiddleware<Foundatio.Mediator.Tests.AsyncCallSyncHandlerTestMiddleware>(serviceProvider);
            object? beforeResult = null;
            string? handlerResult = null;
            Exception? exception = null;
            try
            {
                beforeResult = middlewareInstance.Before(message);
                if (beforeResult is HandlerResult hr && hr.IsShortCircuited)
                {
                    return (string?)hr.Value ?? string.Empty;
                }
                var handlerInstance = GetOrCreateHandler(serviceProvider);
                handlerResult = handlerInstance.Handle(message);
                await middlewareInstance.After(message, beforeResult);
                return handlerResult;
            }
            catch (Exception ex)
            {
                exception = ex;
                throw;
            }
            finally
            {
            }
        }

        public static async ValueTask<object> UntypedHandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (Foundatio.Mediator.Tests.AsyncCallSyncHandlerTestMessage)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            var result = await Handle(typedMessage, serviceProvider, cancellationToken);
            return result ?? new object();
        }

        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "VIrRtosU5MG9oojCRJKmc84DAABBc3luY0NhbGxTeW5jSGFuZGxlcldpdGhBc3luY01pZGRsZXdhcmVUZXN0LmNz")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\tests\Foundatio.Mediator.Tests\AsyncCallSyncHandlerWithAsyncMiddlewareTest.cs(28,37)
        public static async global::System.Threading.Tasks.ValueTask<string> InterceptInvokeAsync0(this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default)
        {
            var typedMessage = (Foundatio.Mediator.Tests.AsyncCallSyncHandlerTestMessage)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            return await Handle(typedMessage, serviceProvider, cancellationToken);
        }

        private static Foundatio.Mediator.Tests.AsyncCallSyncHandlerTestHandler? _handler;
        private static readonly object _lock = new object();

        private static Foundatio.Mediator.Tests.AsyncCallSyncHandlerTestHandler GetOrCreateHandler(IServiceProvider serviceProvider)
        {
            if (_handler != null)
                return _handler;

            var handlerFromDI = serviceProvider.GetService<Foundatio.Mediator.Tests.AsyncCallSyncHandlerTestHandler>();
            if (handlerFromDI != null)
                return handlerFromDI;

            lock (_lock)
            {
                if (_handler != null)
                    return _handler;

                _handler = ActivatorUtilities.CreateInstance<Foundatio.Mediator.Tests.AsyncCallSyncHandlerTestHandler>(serviceProvider);
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
