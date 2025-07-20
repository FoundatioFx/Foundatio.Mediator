#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class SendWelcomeEmailHandler_HandleAsync_SendWelcomeEmailCommand_StaticWrapper
    {
        public static async System.Threading.Tasks.Task HandleAsync(ConsoleSample.Messages.SendWelcomeEmailCommand message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var globalMiddleware = GetOrCreateMiddleware<ConsoleSample.Middleware.GlobalMiddleware>(serviceProvider);
            var loggingMiddleware = GetOrCreateMiddleware<ConsoleSample.Middleware.LoggingMiddleware>(serviceProvider);
            var beforeResults = new object?[2];
#pragma warning disable CS0219 // Variable assigned but never used
            object? handlerResult = null;
#pragma warning restore CS0219
            Exception? exception = null;
            try
            {
                beforeResults[0] = globalMiddleware.Before(message, cancellationToken);
                beforeResults[1] = loggingMiddleware.Before(message);

                var handlerInstance = GetOrCreateHandler(serviceProvider);
                await handlerInstance.HandleAsync(message, serviceProvider.GetRequiredService<ConsoleSample.Services.IEmailService>(), serviceProvider.GetRequiredService<ConsoleSample.Services.IGreetingService>(), cancellationToken);
                await globalMiddleware.After(message, (((System.DateTime Date, System.TimeSpan Time))beforeResults[0]!).Item1, (((System.DateTime Date, System.TimeSpan Time))beforeResults[0]!).Item2, serviceProvider.GetRequiredService<ConsoleSample.Services.IEmailService>(), cancellationToken);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw;
            }
            finally
            {
                loggingMiddleware.Finally(message, ((System.Diagnostics.Stopwatch)beforeResults[1]!), exception);
                globalMiddleware.Finally(message, exception, cancellationToken);
            }
        }

        public static async ValueTask<object> UntypedHandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (ConsoleSample.Messages.SendWelcomeEmailCommand)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            await HandleAsync(typedMessage, serviceProvider, cancellationToken);
            return new object();
        }

        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "tT79g+aRQJPFlT6soNnraKkFAABTYW1wbGVSdW5uZXIuY3M=")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\samples\ConsoleSample\SampleRunner.cs(47,25)
        public static async global::System.Threading.Tasks.ValueTask InterceptInvokeAsync0(this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default)
        {
            var typedMessage = (ConsoleSample.Messages.SendWelcomeEmailCommand)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            await HandleAsync(typedMessage, serviceProvider, cancellationToken);
        }

        private static ConsoleSample.Handlers.SendWelcomeEmailHandler? _handler;
        private static readonly object _lock = new object();

        private static ConsoleSample.Handlers.SendWelcomeEmailHandler GetOrCreateHandler(IServiceProvider serviceProvider)
        {
            if (_handler != null)
                return _handler;

            var handlerFromDI = serviceProvider.GetService<ConsoleSample.Handlers.SendWelcomeEmailHandler>();
            if (handlerFromDI != null)
                return handlerFromDI;

            lock (_lock)
            {
                if (_handler != null)
                    return _handler;

                _handler = ActivatorUtilities.CreateInstance<ConsoleSample.Handlers.SendWelcomeEmailHandler>(serviceProvider);
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
