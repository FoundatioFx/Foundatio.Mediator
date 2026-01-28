using System.Collections.Concurrent;
using System.Reflection;
using Foundatio.Mediator;
using Foundatio.Resilience;
using Microsoft.Extensions.Logging;

namespace ConsoleSample.Middleware;

/// <summary>
/// Middleware that wraps the entire pipeline (Before → Handler → After → Finally) with retry logic.
/// Only applies to messages that implement IRetryableMessage (compile-time filtering).
/// Uses HandlerExecutionInfo to discover retry settings from [Retryable] attribute on handlers.
/// </summary>
[Middleware(Order = 0)] // Low order = outermost wrapper
public static class RetryMiddleware
{
    // Default policy for handlers without [Retryable] attribute
    private static readonly IResiliencePolicy DefaultPolicy = new ResiliencePolicyBuilder()
        .WithMaxAttempts(3)
        .WithExponentialDelay(TimeSpan.FromMilliseconds(100))
        .WithJitter()
        .Build();

    // Cache policies per handler method to avoid rebuilding on each invocation
    private static readonly ConcurrentDictionary<MethodInfo, IResiliencePolicy> PolicyCache = new();

    public static async ValueTask<object?> ExecuteAsync(
        IRetryableMessage message,
        HandlerExecutionDelegate next,
        HandlerExecutionInfo handlerInfo,
        ILogger<IMediator> logger)
    {
        // Get or create the retry policy for this handler
        var policy = PolicyCache.GetOrAdd(handlerInfo.HandlerMethod, method =>
        {
            // Check method first, then class for [Retryable] attribute
            var attr = method.GetCustomAttribute<RetryableAttribute>()
                ?? handlerInfo.HandlerType.GetCustomAttribute<RetryableAttribute>();

            if (attr == null)
                return DefaultPolicy; // Use defaults when no attribute

            var builder = new ResiliencePolicyBuilder()
                .WithMaxAttempts(attr.MaxAttempts);

            if (attr.UseExponentialBackoff)
                builder.WithExponentialDelay(TimeSpan.FromMilliseconds(attr.DelayMs));
            else
                builder.WithLinearDelay(TimeSpan.FromMilliseconds(attr.DelayMs));

            if (attr.UseJitter)
                builder.WithJitter();

            return builder.Build();
        });

        logger.LogDebug("RetryMiddleware: Starting execution for {MessageType}",
            message.GetType().Name);

        return await policy.ExecuteAsync(async ct =>
        {
            logger.LogDebug("RetryMiddleware: Attempt for {MessageType}", message.GetType().Name);
            return await next();
        }, default);
    }
}
