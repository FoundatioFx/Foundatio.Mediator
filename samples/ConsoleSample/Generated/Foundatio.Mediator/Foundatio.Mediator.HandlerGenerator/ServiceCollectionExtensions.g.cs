using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMediator(this IServiceCollection services)
        {
            services.AddSingleton<IMediator, Mediator>();

            // Register all discovered handlers
            services.AddScoped<ConsoleSample.Handlers.SyncCalculationHandler>();
            services.AddScoped<ConsoleSample.Handlers.AsyncCalculationHandler>();
            services.AddScoped<ConsoleSample.Handlers.SendWelcomeEmailHandler>();
            services.AddScoped<ConsoleSample.Handlers.CreatePersonalizedGreetingHandler>();
            services.AddScoped<ConsoleSample.Handlers.OrderEmailNotificationHandler>();
            services.AddScoped<ConsoleSample.Handlers.OrderSmsNotificationHandler>();
            services.AddScoped<ConsoleSample.Handlers.OrderAuditHandler>();
            services.AddScoped<ConsoleSample.Handlers.ProcessOrderHandler>();
            services.AddScoped<ConsoleSample.Handlers.PingHandler>();
            services.AddScoped<ConsoleSample.Handlers.GreetingHandler>();

            // Register handler registrations containing wrapper handlers with metadata
            services.AddScoped<SyncCalculationHandler_Handle_Wrapper>();
            services.AddScoped<HandlerRegistration<ConsoleSample.Messages.SyncCalculationQuery>>(sp =>
                new HandlerRegistration<ConsoleSample.Messages.SyncCalculationQuery>(
                    sp.GetRequiredService<SyncCalculationHandler_Handle_Wrapper>(), false));
            services.AddScoped<AsyncCalculationHandler_HandleAsync_Wrapper>();
            services.AddScoped<HandlerRegistration<ConsoleSample.Messages.AsyncCalculationQuery>>(sp =>
                new HandlerRegistration<ConsoleSample.Messages.AsyncCalculationQuery>(
                    sp.GetRequiredService<AsyncCalculationHandler_HandleAsync_Wrapper>(), true));
            services.AddScoped<SendWelcomeEmailHandler_HandleAsync_Wrapper>();
            services.AddScoped<HandlerRegistration<ConsoleSample.Messages.SendWelcomeEmailCommand>>(sp =>
                new HandlerRegistration<ConsoleSample.Messages.SendWelcomeEmailCommand>(
                    sp.GetRequiredService<SendWelcomeEmailHandler_HandleAsync_Wrapper>(), true));
            services.AddScoped<CreatePersonalizedGreetingHandler_HandleAsync_Wrapper>();
            services.AddScoped<HandlerRegistration<ConsoleSample.Messages.CreatePersonalizedGreetingQuery>>(sp =>
                new HandlerRegistration<ConsoleSample.Messages.CreatePersonalizedGreetingQuery>(
                    sp.GetRequiredService<CreatePersonalizedGreetingHandler_HandleAsync_Wrapper>(), true));
            services.AddScoped<OrderEmailNotificationHandler_HandleAsync_Wrapper>();
            services.AddScoped<HandlerRegistration<ConsoleSample.Messages.OrderCreatedEvent>>(sp =>
                new HandlerRegistration<ConsoleSample.Messages.OrderCreatedEvent>(
                    sp.GetRequiredService<OrderEmailNotificationHandler_HandleAsync_Wrapper>(), true));
            services.AddScoped<OrderSmsNotificationHandler_HandleAsync_Wrapper>();
            services.AddScoped<HandlerRegistration<ConsoleSample.Messages.OrderCreatedEvent>>(sp =>
                new HandlerRegistration<ConsoleSample.Messages.OrderCreatedEvent>(
                    sp.GetRequiredService<OrderSmsNotificationHandler_HandleAsync_Wrapper>(), true));
            services.AddScoped<OrderAuditHandler_HandleAsync_Wrapper>();
            services.AddScoped<HandlerRegistration<ConsoleSample.Messages.OrderCreatedEvent>>(sp =>
                new HandlerRegistration<ConsoleSample.Messages.OrderCreatedEvent>(
                    sp.GetRequiredService<OrderAuditHandler_HandleAsync_Wrapper>(), true));
            services.AddScoped<ProcessOrderHandler_HandleAsync_Wrapper>();
            services.AddScoped<HandlerRegistration<ConsoleSample.Messages.ProcessOrderCommand>>(sp =>
                new HandlerRegistration<ConsoleSample.Messages.ProcessOrderCommand>(
                    sp.GetRequiredService<ProcessOrderHandler_HandleAsync_Wrapper>(), true));
            services.AddScoped<PingHandler_HandleAsync_Wrapper>();
            services.AddScoped<HandlerRegistration<ConsoleSample.Messages.PingCommand>>(sp =>
                new HandlerRegistration<ConsoleSample.Messages.PingCommand>(
                    sp.GetRequiredService<PingHandler_HandleAsync_Wrapper>(), true));
            services.AddScoped<GreetingHandler_Handle_Wrapper>();
            services.AddScoped<HandlerRegistration<ConsoleSample.Messages.GreetingQuery>>(sp =>
                new HandlerRegistration<ConsoleSample.Messages.GreetingQuery>(
                    sp.GetRequiredService<GreetingHandler_Handle_Wrapper>(), false));

            return services;
        }
    }
}
