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

        return services;
    }
}
