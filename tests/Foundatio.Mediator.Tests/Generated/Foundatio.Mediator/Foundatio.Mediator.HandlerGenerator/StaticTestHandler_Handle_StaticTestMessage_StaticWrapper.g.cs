#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class StaticTestHandler_Handle_StaticTestMessage_StaticWrapper
    {
        public static ValueTask HandleTyped(Foundatio.Mediator.Tests.StaticHandlerTest.StaticTestMessage message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            Foundatio.Mediator.Tests.StaticHandlerTest.StaticTestHandler.Handle(message);
            return ValueTask.CompletedTask;
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public static async ValueTask<object> HandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (Foundatio.Mediator.Tests.StaticHandlerTest.StaticTestMessage)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            HandleTyped(typedMessage, serviceProvider, cancellationToken);
            return new object();
        }
#pragma warning restore CS1998

        // Interceptor method
        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "BleR//YJKrBvz9p6O41ZsiQHAABTdGF0aWNIYW5kbGVyVGVzdC5jcw==")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\tests\Foundatio.Mediator.Tests\StaticHandlerTest.cs(54,24)
        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "BleR//YJKrBvz9p6O41ZsjYJAABTdGF0aWNIYW5kbGVyVGVzdC5jcw==")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\tests\Foundatio.Mediator.Tests\StaticHandlerTest.cs(68,24)
        public static async global::System.Threading.Tasks.ValueTask InterceptInvokeAsync0(this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default)
        {
            // Call the strongly typed handler directly for maximum performance
            var typedMessage = (Foundatio.Mediator.Tests.StaticHandlerTest.StaticTestMessage)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            await HandleTyped(typedMessage, serviceProvider, cancellationToken);
        }
    }
}
