using System.Collections.Concurrent;
using System.Reflection;
using Foundatio.Mediator;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Common.Module.Middleware;

/// <summary>
/// Execute middleware that caches handler results using .NET's <see cref="IMemoryCache"/>.
/// Only applies to handlers decorated with <see cref="CachedAttribute"/> (ExplicitOnly = true).
/// Because C# records use value equality, identical query messages produce the same cache key automatically.
/// </summary>
[Middleware(Order = 100, ExplicitOnly = true)]
public class CachingMiddleware
{
    private static readonly ConcurrentDictionary<MethodInfo, CacheSettings> SettingsCache = new();
    private static CachingMiddleware? _instance;

    /// <summary>Tracks active cache keys so <see cref="Clear"/> can remove them all.</summary>
    private readonly ConcurrentDictionary<string, byte> _keys = new();
    private readonly IMemoryCache _cache;
    private readonly ILogger<IMediator> _logger;

    public CachingMiddleware(IMemoryCache cache, ILogger<IMediator> logger)
    {
        _cache = cache;
        _logger = logger;
        _instance = this;
    }

    /// <summary>Derives a stable string cache key from a message using its type and value-based hash code.</summary>
    private static string GetCacheKey(object message)
        => $"mediator:{message.GetType().FullName}:{message.GetHashCode()}";

    public async ValueTask<object?> ExecuteAsync(
        object message,
        HandlerExecutionDelegate next,
        HandlerExecutionInfo handlerInfo)
    {
        var settings = SettingsCache.GetOrAdd(handlerInfo.HandlerMethod, method =>
        {
            var attr = method.GetCustomAttribute<CachedAttribute>();
            return new CacheSettings
            {
                Duration = TimeSpan.FromSeconds(attr?.DurationSeconds ?? 300),
                SlidingExpiration = attr?.SlidingExpiration ?? false
            };
        });

        var cacheKey = GetCacheKey(message);

        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            _logger.LogDebug("CachingMiddleware: Cache HIT for {MessageType}", message.GetType().Name);
            return cached;
        }

        // Cache miss — execute the full pipeline
        _logger.LogDebug("CachingMiddleware: Cache MISS for {MessageType}, executing handler", message.GetType().Name);
        var result = await next();

        var options = new MemoryCacheEntryOptions()
            .RegisterPostEvictionCallback((key, _, _, _) => _keys.TryRemove((string)key, out _));

        if (settings.SlidingExpiration)
            options.SetSlidingExpiration(settings.Duration);
        else
            options.SetAbsoluteExpiration(settings.Duration);

        _cache.Set(cacheKey, result, options);
        _keys.TryAdd(cacheKey, 0);

        return result;
    }

    /// <summary>Removes a specific message's cached result.</summary>
    public static void Invalidate(object message)
    {
        if (_instance is not { } instance) return;
        var key = GetCacheKey(message);
        instance._cache.Remove(key);
        instance._keys.TryRemove(key, out _);
    }

    /// <summary>Clears all mediator-cached entries.</summary>
    public static void Clear()
    {
        if (_instance is not { } instance) return;
        foreach (var key in instance._keys.Keys)
            instance._cache.Remove(key);
        instance._keys.Clear();
    }

    private sealed class CacheSettings
    {
        public TimeSpan Duration { get; init; }
        public bool SlidingExpiration { get; init; }
    }
}
