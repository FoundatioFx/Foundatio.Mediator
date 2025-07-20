#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class FoundatioCreateOrderHandler_HandleAsync_CreateOrderCommand_StaticWrapper
    {
        public static async System.Threading.Tasks.Task<string> HandleAsync(Foundatio.Mediator.Benchmarks.Messages.CreateOrderCommand message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var handlerInstance = GetOrCreateHandler(serviceProvider);
            return await handlerInstance.HandleAsync(message, cancellationToken);
        }

        public static async ValueTask<object> UntypedHandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (Foundatio.Mediator.Benchmarks.Messages.CreateOrderCommand)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            var result = await HandleAsync(typedMessage, serviceProvider, cancellationToken);
            return result ?? new object();
        }

        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "OexwaUhSBGpnrGeZ1ynkbOMaAABNZWRpYXRvckJlbmNobWFya3MuY3M=")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\benchmarks\Foundatio.Mediator.Benchmarks\MediatorBenchmarks.cs(181,41)
        public static async global::System.Threading.Tasks.ValueTask<string> InterceptInvokeAsync0(this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default)
        {
            var typedMessage = (Foundatio.Mediator.Benchmarks.Messages.CreateOrderCommand)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            return await HandleAsync(typedMessage, serviceProvider, cancellationToken);
        }

        private static Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioCreateOrderHandler? _handler;
        private static readonly object _lock = new object();

        private static Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioCreateOrderHandler GetOrCreateHandler(IServiceProvider serviceProvider)
        {
            if (_handler != null)
                return _handler;

            var handlerFromDI = serviceProvider.GetService<Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioCreateOrderHandler>();
            if (handlerFromDI != null)
                return handlerFromDI;

            lock (_lock)
            {
                if (_handler != null)
                    return _handler;

                _handler = ActivatorUtilities.CreateInstance<Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioCreateOrderHandler>(serviceProvider);
                return _handler;
            }
        }
    }
}
