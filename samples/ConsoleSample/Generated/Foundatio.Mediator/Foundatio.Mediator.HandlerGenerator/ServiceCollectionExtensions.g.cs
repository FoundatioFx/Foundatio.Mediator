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

            services.AddKeyedSingleton<HandlerRegistration>("ConsoleSample.Messages.SyncCalculationQuery",
                new HandlerRegistration(
                    "ConsoleSample.Messages.SyncCalculationQuery",
                    SyncCalculationHandler_Handle_SyncCalculationQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<ConsoleSample.Messages.SyncCalculationQuery>",
                new HandlerRegistration(
                    "ConsoleSample.Messages.SyncCalculationQuery",
                    SyncCalculationHandler_Handle_SyncCalculationQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("ConsoleSample.Messages.AsyncCalculationQuery",
                new HandlerRegistration(
                    "ConsoleSample.Messages.AsyncCalculationQuery",
                    AsyncCalculationHandler_HandleAsync_AsyncCalculationQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<ConsoleSample.Messages.AsyncCalculationQuery>",
                new HandlerRegistration(
                    "ConsoleSample.Messages.AsyncCalculationQuery",
                    AsyncCalculationHandler_HandleAsync_AsyncCalculationQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("ConsoleSample.Messages.SendWelcomeEmailCommand",
                new HandlerRegistration(
                    "ConsoleSample.Messages.SendWelcomeEmailCommand",
                    SendWelcomeEmailHandler_HandleAsync_SendWelcomeEmailCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<ConsoleSample.Messages.SendWelcomeEmailCommand>",
                new HandlerRegistration(
                    "ConsoleSample.Messages.SendWelcomeEmailCommand",
                    SendWelcomeEmailHandler_HandleAsync_SendWelcomeEmailCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("ConsoleSample.Messages.CreatePersonalizedGreetingQuery",
                new HandlerRegistration(
                    "ConsoleSample.Messages.CreatePersonalizedGreetingQuery",
                    CreatePersonalizedGreetingHandler_HandleAsync_CreatePersonalizedGreetingQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<ConsoleSample.Messages.CreatePersonalizedGreetingQuery>",
                new HandlerRegistration(
                    "ConsoleSample.Messages.CreatePersonalizedGreetingQuery",
                    CreatePersonalizedGreetingHandler_HandleAsync_CreatePersonalizedGreetingQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("ConsoleSample.Messages.OrderCreatedEvent",
                new HandlerRegistration(
                    "ConsoleSample.Messages.OrderCreatedEvent",
                    OrderEmailNotificationHandler_HandleAsync_OrderCreatedEvent_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<ConsoleSample.Messages.OrderCreatedEvent>",
                new HandlerRegistration(
                    "ConsoleSample.Messages.OrderCreatedEvent",
                    OrderEmailNotificationHandler_HandleAsync_OrderCreatedEvent_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("ConsoleSample.Messages.OrderCreatedEvent",
                new HandlerRegistration(
                    "ConsoleSample.Messages.OrderCreatedEvent",
                    OrderSmsNotificationHandler_HandleAsync_OrderCreatedEvent_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<ConsoleSample.Messages.OrderCreatedEvent>",
                new HandlerRegistration(
                    "ConsoleSample.Messages.OrderCreatedEvent",
                    OrderSmsNotificationHandler_HandleAsync_OrderCreatedEvent_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("ConsoleSample.Messages.OrderCreatedEvent",
                new HandlerRegistration(
                    "ConsoleSample.Messages.OrderCreatedEvent",
                    OrderAuditHandler_HandleAsync_OrderCreatedEvent_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<ConsoleSample.Messages.OrderCreatedEvent>",
                new HandlerRegistration(
                    "ConsoleSample.Messages.OrderCreatedEvent",
                    OrderAuditHandler_HandleAsync_OrderCreatedEvent_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("ConsoleSample.Messages.ProcessOrderCommand",
                new HandlerRegistration(
                    "ConsoleSample.Messages.ProcessOrderCommand",
                    ProcessOrderHandler_HandleAsync_ProcessOrderCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.ICommand",
                new HandlerRegistration(
                    "ConsoleSample.Messages.ProcessOrderCommand",
                    ProcessOrderHandler_HandleAsync_ProcessOrderCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<ConsoleSample.Messages.ProcessOrderCommand>",
                new HandlerRegistration(
                    "ConsoleSample.Messages.ProcessOrderCommand",
                    ProcessOrderHandler_HandleAsync_ProcessOrderCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("ConsoleSample.Messages.PingCommand",
                new HandlerRegistration(
                    "ConsoleSample.Messages.PingCommand",
                    PingHandler_HandleAsync_PingCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.ICommand",
                new HandlerRegistration(
                    "ConsoleSample.Messages.PingCommand",
                    PingHandler_HandleAsync_PingCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<ConsoleSample.Messages.PingCommand>",
                new HandlerRegistration(
                    "ConsoleSample.Messages.PingCommand",
                    PingHandler_HandleAsync_PingCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("ConsoleSample.Messages.GreetingQuery",
                new HandlerRegistration(
                    "ConsoleSample.Messages.GreetingQuery",
                    GreetingHandler_Handle_GreetingQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("System.IEquatable<ConsoleSample.Messages.GreetingQuery>",
                new HandlerRegistration(
                    "ConsoleSample.Messages.GreetingQuery",
                    GreetingHandler_Handle_GreetingQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));

            return services;
        }
    }
}
