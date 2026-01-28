using Foundatio.Mediator;
using Foundatio.Resilience;
using Microsoft.Extensions.Logging;

namespace ConsoleSample.Middleware;

/// <summary>
/// Middleware that wraps the entire pipeline (Before → Handler → After → Finally) with retry logic.
/// Only applies to messages that implement IRetryableMessage.
/// </summary>
[Middleware(Order = 0)] // Low order = outermost wrapper
public static class RetryMiddleware
{
    // Static policy - simple configuration for sample
    private static readonly IResiliencePolicy Policy = new ResiliencePolicyBuilder()
        .WithMaxAttempts(3)
        .WithExponentialDelay(TimeSpan.FromMilliseconds(100))
        .WithJitter()
        .Build();

    public static async ValueTask<object?> ExecuteAsync(
        IRetryableMessage message,
        HandlerExecutionDelegate next,
        ILogger<IMediator> logger)
    {
        logger.LogDebug("RetryMiddleware: Starting execution for {MessageType}", message.GetType().Name);

        return await Policy.ExecuteAsync(async ct =>
        {
            logger.LogDebug("RetryMiddleware: Attempt for {MessageType}", message.GetType().Name);
            return await next();
        }, default);
    }
}
