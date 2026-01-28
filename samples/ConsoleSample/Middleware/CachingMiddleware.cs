using System.Collections.Concurrent;
using System.Reflection;
using Foundatio.Mediator;
using Microsoft.Extensions.Logging;

namespace ConsoleSample.Middleware;

/// <summary>
/// Middleware that caches handler responses to avoid repeated execution.
/// Only applies to handlers that use the [Cached] attribute (ExplicitOnly = true).
/// Uses the message as the cache key (message type must implement value equality - records work automatically).
/// </summary>
[Middleware(Order = 100, ExplicitOnly = true)] // High order = runs close to handler (innermost)
public static class CachingMiddleware
{
    private static readonly ConcurrentDictionary<object, CacheEntry> Cache = new();

    // Cache settings per handler method
    private static readonly ConcurrentDictionary<MethodInfo, CacheSettings> SettingsCache = new();

    public static async ValueTask<object?> ExecuteAsync(
        object message,
        HandlerExecutionDelegate next,
        HandlerExecutionInfo handlerInfo,
        ILogger<IMediator> logger)
    {
        // Get cache settings for this handler
        var settings = SettingsCache.GetOrAdd(handlerInfo.HandlerMethod, method =>
        {
            var attr = method.GetCustomAttribute<CachedAttribute>();
            return new CacheSettings
            {
                Duration = TimeSpan.FromSeconds(attr?.DurationSeconds ?? 300),
                SlidingExpiration = attr?.SlidingExpiration ?? false
            };
        });

        // Try to get from cache
        if (Cache.TryGetValue(message, out var entry))
        {
            if (!entry.IsExpired)
            {
                logger.LogDebug("CachingMiddleware: Cache HIT for {MessageType}", message.GetType().Name);

                // Update access time for sliding expiration
                if (settings.SlidingExpiration)
                {
                    entry.LastAccessed = DateTime.UtcNow;
                }

                return entry.Value;
            }

            // Entry expired, remove it
            Cache.TryRemove(message, out _);
            logger.LogDebug("CachingMiddleware: Cache EXPIRED for {MessageType}", message.GetType().Name);
        }

        // Cache miss - execute handler
        logger.LogDebug("CachingMiddleware: Cache MISS for {MessageType}, executing handler", message.GetType().Name);
        var result = await next();

        // Store in cache
        Cache[message] = new CacheEntry
        {
            Value = result,
            CreatedAt = DateTime.UtcNow,
            LastAccessed = DateTime.UtcNow,
            Duration = settings.Duration,
            SlidingExpiration = settings.SlidingExpiration
        };

        // Cleanup old entries periodically (simple approach - in production use IMemoryCache)
        if (Cache.Count > 1000)
        {
            CleanupExpiredEntries();
        }

        return result;
    }

    /// <summary>
    /// Invalidates the cache entry for a specific message.
    /// Call this when data changes (e.g., after update/delete operations).
    /// </summary>
    public static void Invalidate(object message)
    {
        Cache.TryRemove(message, out _);
    }

    /// <summary>
    /// Clears all cached entries.
    /// </summary>
    public static void Clear()
    {
        Cache.Clear();
    }

    private static void CleanupExpiredEntries()
    {
        var expiredKeys = Cache.Where(kvp => kvp.Value.IsExpired).Select(kvp => kvp.Key).ToList();
        foreach (var key in expiredKeys)
        {
            Cache.TryRemove(key, out _);
        }
    }

    private sealed class CacheEntry
    {
        public object? Value { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime LastAccessed { get; set; }
        public TimeSpan Duration { get; init; }
        public bool SlidingExpiration { get; init; }

        public bool IsExpired => SlidingExpiration
            ? DateTime.UtcNow - LastAccessed > Duration
            : DateTime.UtcNow - CreatedAt > Duration;
    }

    private sealed class CacheSettings
    {
        public TimeSpan Duration { get; init; }
        public bool SlidingExpiration { get; init; }
    }
}
