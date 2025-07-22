#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class FoundatioGetOrderDetailsHandler_HandleAsync_GetOrderDetailsQuery_StaticWrapper
    {
        public static async System.Threading.Tasks.Task<Foundatio.Mediator.Benchmarks.Messages.OrderDetails> HandleAsync(Foundatio.Mediator.Benchmarks.Messages.GetOrderDetailsQuery message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var handlerInstance = GetOrCreateHandler(serviceProvider);
            return await handlerInstance.HandleAsync(message);
        }

        public static async ValueTask<object> UntypedHandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (Foundatio.Mediator.Benchmarks.Messages.GetOrderDetailsQuery)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            var result = await HandleAsync(typedMessage, serviceProvider, cancellationToken);
            return result ?? new object();
        }

        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "h2bOfUqPvgXfRKAe5B6n1YsdAABNZWRpYXRvckJlbmNobWFya3MuY3M=")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\benchmarks\Foundatio.Mediator.Benchmarks\MediatorBenchmarks.cs(202,41)
        public static async global::System.Threading.Tasks.ValueTask<Foundatio.Mediator.Benchmarks.Messages.OrderDetails> InterceptInvokeAsync0(this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default)
        {
            var typedMessage = (Foundatio.Mediator.Benchmarks.Messages.GetOrderDetailsQuery)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            return await HandleAsync(typedMessage, serviceProvider, cancellationToken);
        }

        private static Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioGetOrderDetailsHandler? _handler;
        private static readonly object _lock = new object();

        private static Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioGetOrderDetailsHandler GetOrCreateHandler(IServiceProvider serviceProvider)
        {
            if (_handler != null)
                return _handler;

            var handlerFromDI = serviceProvider.GetService<Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioGetOrderDetailsHandler>();
            if (handlerFromDI != null)
                return handlerFromDI;

            lock (_lock)
            {
                if (_handler != null)
                    return _handler;

                _handler = ActivatorUtilities.CreateInstance<Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioGetOrderDetailsHandler>(serviceProvider);
                return _handler;
            }
        }
    }
}
