#nullable enable
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Foundatio.Mediator
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMediator(this IServiceCollection services)
        {
            services.AddSingleton<IMediator, Mediator>();

            // Register HandlerRegistration instances keyed by message type name
            // Note: Handlers themselves are NOT auto-registered in DI
            // Users can register them manually if they want specific lifetimes

            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Benchmarks.Messages.PingCommand",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.PingCommand",
                    FoundatioPingHandler_HandleAsync_PingCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("MediatR.IRequest",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.PingCommand",
                    FoundatioPingHandler_HandleAsync_PingCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("MediatR.IBaseRequest",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.PingCommand",
                    FoundatioPingHandler_HandleAsync_PingCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<Foundatio.Mediator.Benchmarks.Messages.PingCommand>",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.PingCommand",
                    FoundatioPingHandler_HandleAsync_PingCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Benchmarks.Messages.GreetingQuery",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.GreetingQuery",
                    FoundatioGreetingHandler_HandleAsync_GreetingQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("MediatR.IRequest<string>",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.GreetingQuery",
                    FoundatioGreetingHandler_HandleAsync_GreetingQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("MediatR.IBaseRequest",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.GreetingQuery",
                    FoundatioGreetingHandler_HandleAsync_GreetingQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<Foundatio.Mediator.Benchmarks.Messages.GreetingQuery>",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.GreetingQuery",
                    FoundatioGreetingHandler_HandleAsync_GreetingQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Benchmarks.Messages.CreateOrderCommand",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.CreateOrderCommand",
                    FoundatioCreateOrderHandler_HandleAsync_CreateOrderCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("MediatR.IRequest<string>",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.CreateOrderCommand",
                    FoundatioCreateOrderHandler_HandleAsync_CreateOrderCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("MediatR.IBaseRequest",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.CreateOrderCommand",
                    FoundatioCreateOrderHandler_HandleAsync_CreateOrderCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<Foundatio.Mediator.Benchmarks.Messages.CreateOrderCommand>",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.CreateOrderCommand",
                    FoundatioCreateOrderHandler_HandleAsync_CreateOrderCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Benchmarks.Messages.GetOrderDetailsQuery",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.GetOrderDetailsQuery",
                    FoundatioGetOrderDetailsHandler_HandleAsync_GetOrderDetailsQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("MediatR.IRequest<Foundatio.Mediator.Benchmarks.Messages.OrderDetails>",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.GetOrderDetailsQuery",
                    FoundatioGetOrderDetailsHandler_HandleAsync_GetOrderDetailsQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("MediatR.IBaseRequest",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.GetOrderDetailsQuery",
                    FoundatioGetOrderDetailsHandler_HandleAsync_GetOrderDetailsQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<Foundatio.Mediator.Benchmarks.Messages.GetOrderDetailsQuery>",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.GetOrderDetailsQuery",
                    FoundatioGetOrderDetailsHandler_HandleAsync_GetOrderDetailsQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent",
                    FoundatioUserRegisteredEmailHandler_HandleAsync_UserRegisteredEvent_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("MediatR.INotification",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent",
                    FoundatioUserRegisteredEmailHandler_HandleAsync_UserRegisteredEvent_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent>",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent",
                    FoundatioUserRegisteredEmailHandler_HandleAsync_UserRegisteredEvent_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent",
                    FoundatioUserRegisteredAnalyticsHandler_HandleAsync_UserRegisteredEvent_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("MediatR.INotification",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent",
                    FoundatioUserRegisteredAnalyticsHandler_HandleAsync_UserRegisteredEvent_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent>",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent",
                    FoundatioUserRegisteredAnalyticsHandler_HandleAsync_UserRegisteredEvent_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent",
                    FoundatioUserRegisteredWelcomeHandler_HandleAsync_UserRegisteredEvent_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("MediatR.INotification",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent",
                    FoundatioUserRegisteredWelcomeHandler_HandleAsync_UserRegisteredEvent_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent>",
                new HandlerRegistration(
                    "Foundatio.Mediator.Benchmarks.Messages.UserRegisteredEvent",
                    FoundatioUserRegisteredWelcomeHandler_HandleAsync_UserRegisteredEvent_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));

            return services;
        }
    }
}
