using System.Collections.Concurrent;
using System.Reflection;
using Foundatio.Mediator;
using Foundatio.Resilience;
using Microsoft.Extensions.Logging;

namespace Common.Module.Middleware;

/// <summary>
/// Middleware that wraps the entire pipeline (Before → Handler → After → Finally) with retry logic.
/// Only applies to handlers that use the [Retry] attribute (ExplicitOnly = true).
/// Uses HandlerExecutionInfo to discover retry settings from the [Retry] attribute.
///
/// Supports two modes:
/// 1. Named policy — set <c>[Retry(PolicyName = "aggressive")]</c> to look up a
///    pre-configured policy from <see cref="IResiliencePolicyProvider"/>.
/// 2. Inline settings — set properties like <c>[Retry(MaxAttempts = 5)]</c> to
///    build a one-off policy from the attribute values.
/// When <c>PolicyName</c> is set it takes priority and all other attribute properties are ignored.
/// </summary>
[Middleware(Order = 0, ExplicitOnly = true, OrderBefore = [typeof(CachingMiddleware)])] // Outermost wrapper, runs before caching so retried results can still be cached
public static class RetryMiddleware
{
    // Default policy for handlers without custom settings and no named policy
    private static readonly IResiliencePolicy DefaultPolicy = new ResiliencePolicyBuilder()
        .WithMaxAttempts(3)
        .WithExponentialDelay(TimeSpan.FromMilliseconds(100))
        .WithJitter()
        .Build();

    // Cache policies per handler method to avoid rebuilding on each invocation.
    // Named policies are resolved once and cached here too.
    private static readonly ConcurrentDictionary<MethodInfo, IResiliencePolicy> PolicyCache = new();

    public static async ValueTask<object?> ExecuteAsync(
        object message,
        HandlerExecutionDelegate next,
        HandlerExecutionInfo handlerInfo,
        IResiliencePolicyProvider policyProvider,
        ILogger<IMediator> logger)
    {
        // Get or create the retry policy for this handler
        var policy = PolicyCache.GetOrAdd(handlerInfo.HandlerMethod, method =>
        {
            // Check method first, then class for [Retry] attribute
            var attr = method.GetCustomAttribute<RetryAttribute>()
                ?? handlerInfo.HandlerType.GetCustomAttribute<RetryAttribute>();

            // 1. Named policy — look it up from the provider
            if (!string.IsNullOrEmpty(attr?.PolicyName))
                return policyProvider.GetPolicy(attr.PolicyName) ?? DefaultPolicy;

            // 2. No attribute at all — use built-in defaults
            if (attr == null)
                return DefaultPolicy;

            // 3. Inline settings from the attribute
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
