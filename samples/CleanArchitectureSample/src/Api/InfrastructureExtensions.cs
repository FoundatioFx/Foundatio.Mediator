using Foundatio.Mediator.Distributed;
using Microsoft.AspNetCore.Authentication.Cookies;
using StackExchange.Redis;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Infrastructure extension methods for the Api host.
/// </summary>
public static class InfrastructureExtensions
{
    /// <summary>
    /// Registers Redis (<see cref="IConnectionMultiplexer"/>, distributed cache) and HybridCache.
    /// </summary>
    public static WebApplicationBuilder AddRedisAndCaching(this WebApplicationBuilder builder)
    {
        var redisConnection = builder.Configuration.GetConnectionString("redis")
            ?? throw new InvalidOperationException(
                "A 'redis' connection string is required. Set ConnectionStrings__redis or provide via Aspire.");

        builder.Services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConnection));

        builder.Services.AddStackExchangeRedisCache(options =>
            options.Configuration = redisConnection);

        builder.Services.AddHybridCache();

        return builder;
    }

    /// <summary>
    /// Adds cookie authentication configured for API usage (returns 401/403 JSON instead of redirects).
    /// </summary>
    public static WebApplicationBuilder AddSampleAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "ModularMonolith.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });
        builder.Services.AddAuthorization();

        return builder;
    }

    /// <summary>
    /// Logs worker/queue information at startup for dashboard visibility.
    /// </summary>
    public static WebApplication LogStartupDiagnostics(this WebApplication app, AppOptions options)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

        logger.LogInformation("Running in {Mode} mode (queues: {Queues})",
            options.Mode,
            options.Queues is { Count: > 0 } ? string.Join(", ", options.Queues) : "all");

        var workerRegistry = app.Services.GetService<IQueueWorkerRegistry>();
        if (workerRegistry is not null)
        {
            var allWorkers = workerRegistry.GetWorkers();
            var activeWorkers = allWorkers.Where(w => w.WorkerRegistered).ToList();
            logger.LogInformation("Queue workers: {ActiveCount}/{TotalCount} registered ({QueueNames})",
                activeWorkers.Count, allWorkers.Count,
                activeWorkers.Count > 0
                    ? string.Join(", ", activeWorkers.Select(w => w.QueueName))
                    : "none");
        }

        return app;
    }
}
