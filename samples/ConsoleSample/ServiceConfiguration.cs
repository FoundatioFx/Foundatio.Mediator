using ConsoleSample.Services;
using Foundatio.Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConsoleSample;

public static class ServiceConfiguration
{
    public static IServiceCollection ConfigureServices(this IServiceCollection services)
    {
        // Register logging
        services.AddLogging(builder => builder.AddConsole());

        // Register services for dependency injection examples
        services.AddScoped<IGreetingService, GreetingService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddSingleton<EmailNotificationService>();
        services.AddSingleton<SmsNotificationService>();
        services.AddSingleton<IAuditService, AuditService>();

        // Register mediator and handlers
        services.AddMediator();

        // Register middleware (no longer needed - middleware will be auto-created)
        // services.AddScoped<ConsoleSample.Middleware.ProcessOrderMiddleware>();
        // services.AddScoped<ConsoleSample.Middleware.GlobalMiddleware>();
        // services.AddScoped<ConsoleSample.Middleware.CommandMiddleware>();
        // services.AddScoped<ConsoleSample.Middleware.AnotherGlobalMiddleware>();
        // services.AddScoped<ConsoleSample.Middleware.FirstOrderedMiddleware>();
        // services.AddScoped<ConsoleSample.Middleware.LastOrderedMiddleware>();
        // services.AddScoped<ConsoleSample.Middleware.SyncMiddleware>();

        return services;
    }
}
