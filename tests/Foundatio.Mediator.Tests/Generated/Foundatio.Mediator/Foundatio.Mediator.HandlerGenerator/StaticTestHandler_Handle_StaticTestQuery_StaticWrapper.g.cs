#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    internal static class StaticTestHandler_Handle_StaticTestQuery_StaticWrapper
    {
        public static string HandleTyped(Foundatio.Mediator.Tests.StaticHandlerTest.StaticTestQuery message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            return Foundatio.Mediator.Tests.StaticHandlerTest.StaticTestHandler.Handle(message);
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public static async ValueTask<object> HandleAsync(IMediator mediator, object message, CancellationToken cancellationToken, Type? responseType)
        {
            var typedMessage = (Foundatio.Mediator.Tests.StaticHandlerTest.StaticTestQuery)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            var result = HandleTyped(typedMessage, serviceProvider, cancellationToken);
            return result ?? new object();
        }
#pragma warning restore CS1998

        // Interceptor method
        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "BleR//YJKrBvz9p6O41ZsgELAABTdGF0aWNIYW5kbGVyVGVzdC5jcw==")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\tests\Foundatio.Mediator.Tests\StaticHandlerTest.cs(81,37)
        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "BleR//YJKrBvz9p6O41ZshUNAABTdGF0aWNIYW5kbGVyVGVzdC5jcw==")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\tests\Foundatio.Mediator.Tests\StaticHandlerTest.cs(96,37)
        [global::System.Runtime.CompilerServices.InterceptsLocation(1, "BleR//YJKrBvz9p6O41Zso8QAABTdGF0aWNIYW5kbGVyVGVzdC5jcw==")] // C:\Users\eric\Projects\Foundatio\Foundatio.Mediator\tests\Foundatio.Mediator.Tests\StaticHandlerTest.cs(120,37)
        public static async global::System.Threading.Tasks.ValueTask<string> InterceptInvokeAsync0(this global::Foundatio.Mediator.IMediator mediator, object message, global::System.Threading.CancellationToken cancellationToken = default)
        {
            // Call the strongly typed handler directly for maximum performance
            var typedMessage = (Foundatio.Mediator.Tests.StaticHandlerTest.StaticTestQuery)message;
            var serviceProvider = ((Mediator)mediator).ServiceProvider;
            return HandleTyped(typedMessage, serviceProvider, cancellationToken);
        }
    }
}
