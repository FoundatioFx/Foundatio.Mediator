#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class FoundatioPingHandler_HandleAsync_PingCommand_StaticWrapper
    {
        public static async System.Threading.Tasks.Task HandleAsync(Foundatio.Mediator.Benchmarks.Messages.PingCommand message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            var handlerInstance = GetOrCreateHandler(serviceProvider);
            await handlerInstance.HandleAsync(message, cancellationToken);
        }

        public static async ValueTask<object> UntypedHandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (Foundatio.Mediator.Benchmarks.Messages.PingCommand)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            await HandleAsync(typedMessage, serviceProvider, cancellationToken);
            return new object();
        }

        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "h2bOfUqPvgXfRKAe5B6n1QgRAABNZWRpYXRvckJlbmNobWFya3MuY3M=")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\benchmarks\Foundatio.Mediator.Benchmarks\MediatorBenchmarks.cs(98,34)
        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "h2bOfUqPvgXfRKAe5B6n1YkiAABNZWRpYXRvckJlbmNobWFya3MuY3M=")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\benchmarks\Foundatio.Mediator.Benchmarks\MediatorBenchmarks.cs(243,34)
        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "rmueBE/F28JnldzlDiLzcUYLAABTaW1wbGVCZW5jaG1hcmtzLmNz")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\benchmarks\Foundatio.Mediator.Benchmarks\SimpleBenchmarks.cs(72,34)
        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "SQOA9eT++kGm0vwxLS3pA28JAABUaHJvdWdocHV0QmVuY2htYXJrcy5jcw==")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\benchmarks\Foundatio.Mediator.Benchmarks\ThroughputBenchmarks.cs(69,43)
        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "SQOA9eT++kGm0vwxLS3pA0sQAABUaHJvdWdocHV0QmVuY2htYXJrcy5jcw==")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\benchmarks\Foundatio.Mediator.Benchmarks\ThroughputBenchmarks.cs(126,38)
        public static async global::System.Threading.Tasks.ValueTask InterceptInvokeAsync0(this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default)
        {
            var typedMessage = (Foundatio.Mediator.Benchmarks.Messages.PingCommand)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            await HandleAsync(typedMessage, serviceProvider, cancellationToken);
        }

        private static Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioPingHandler? _handler;
        private static readonly object _lock = new object();

        private static Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioPingHandler GetOrCreateHandler(IServiceProvider serviceProvider)
        {
            if (_handler != null)
                return _handler;

            var handlerFromDI = serviceProvider.GetService<Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioPingHandler>();
            if (handlerFromDI != null)
                return handlerFromDI;

            lock (_lock)
            {
                if (_handler != null)
                    return _handler;

                _handler = ActivatorUtilities.CreateInstance<Foundatio.Mediator.Benchmarks.Handlers.Foundatio.FoundatioPingHandler>(serviceProvider);
                return _handler;
            }
        }
    }
}
