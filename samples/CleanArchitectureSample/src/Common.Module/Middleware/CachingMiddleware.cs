using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Foundatio.Mediator;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Common.Module.Middleware;

/// <summary>
/// Execute middleware that caches handler results using .NET's <see cref="HybridCache"/>.
/// Provides L1 (in-memory) + L2 (distributed via IDistributedCache/Redis) caching.
/// Only applies to handlers decorated with <see cref="CachedAttribute"/> (ExplicitOnly = true).
/// Because C# records use value equality, identical query messages produce the same cache key automatically.
/// </summary>
/// <remarks>
/// Values are wrapped in a <see cref="CacheEnvelope"/> that preserves the concrete .NET type name.
/// This is necessary because the middleware operates on <c>object?</c>, and when HybridCache
/// deserializes from the L2 (Redis) cache, System.Text.Json would otherwise produce a
/// <see cref="JsonElement"/> instead of the original type.
/// </remarks>
[Middleware(Order = 100, ExplicitOnly = true)]
public class CachingMiddleware
{
    private static readonly ConcurrentDictionary<MethodInfo, CacheSettings> SettingsCache = new();

    /// <summary>
    /// JSON options that can deserialize types with internal/private init setters (e.g. Result&lt;T&gt;).
    /// Without this modifier, System.Text.Json silently skips properties it cannot set, producing
    /// objects with default values.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                static typeInfo =>
                {
                    if (typeInfo.Kind != JsonTypeInfoKind.Object)
                        return;

                    foreach (var prop in typeInfo.Properties)
                    {
                        if (prop.Set is null && prop.AttributeProvider is PropertyInfo pi)
                        {
                            var setter = pi.GetSetMethod(nonPublic: true);
                            if (setter is not null)
                                prop.Set = (obj, val) => setter.Invoke(obj, [val]);
                        }
                    }
                }
            }
        }
    };

    private static CachingMiddleware? _instance;

    private readonly HybridCache _cache;
    private readonly ILogger<IMediator> _logger;

    public CachingMiddleware(HybridCache cache, ILogger<IMediator> logger)
    {
        _cache = cache;
        _logger = logger;
        _instance = this;
    }

    /// <summary>Derives a stable string cache key from a message using its type and value-based hash code.</summary>
    private static string GetCacheKey(object message)
        => $"mediator:{message.GetType().FullName}:{message.GetHashCode()}";

    /// <summary>Derives a tag from the message type name for group invalidation.</summary>
    private static string GetTag(object message)
        => $"mediator:{message.GetType().Name}";

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
        var tag = GetTag(message);

        var entryOptions = new HybridCacheEntryOptions
        {
            Expiration = settings.Duration,
            LocalCacheExpiration = settings.Duration
        };

        var executed = false;
        var envelope = await _cache.GetOrCreateAsync(
            cacheKey,
            async ct =>
            {
                executed = true;
                _logger.LogInformation("CachingMiddleware: Cache MISS for {MessageType} (key: {CacheKey}), executing handler", message.GetType().Name, cacheKey);
                var result = await next().ConfigureAwait(false);
                return CacheEnvelope.Wrap(result);
            },
            entryOptions,
            [tag],
            cancellationToken: default).ConfigureAwait(false);

        if (!executed)
            _logger.LogInformation("CachingMiddleware: Cache HIT for {MessageType} (key: {CacheKey})", message.GetType().Name, cacheKey);

        return envelope.Unwrap();
    }

    /// <summary>Removes a specific message's cached result from both L1 and L2.</summary>
    public static async Task InvalidateAsync(object message)
    {
        if (_instance is not { } instance)
        {
            return;
        }

        var key = GetCacheKey(message);
        await instance._cache.RemoveAsync(key).ConfigureAwait(false);
        instance._logger.LogInformation("CachingMiddleware: Invalidated {MessageType} (key: {CacheKey})", message.GetType().Name, key);
    }

    /// <summary>Removes all cached results for a message type from both L1 and L2 via tag.</summary>
    public static async Task InvalidateByTagAsync(string tag)
    {
        if (_instance is not { } instance)
        {
            return;
        }

        var fullTag = $"mediator:{tag}";
        await instance._cache.RemoveByTagAsync(fullTag).ConfigureAwait(false);
        instance._logger.LogInformation("CachingMiddleware: Invalidated by tag {Tag}", fullTag);
    }

    /// <summary>Clears all mediator-cached entries from both L1 and L2.</summary>
    public static async Task ClearAsync()
    {
        if (_instance is not { } instance)
        {
            return;
        }

        // The wildcard * tag invalidates all HybridCache data
        await instance._cache.RemoveByTagAsync("*").ConfigureAwait(false);
        instance._logger.LogInformation("CachingMiddleware: Cleared all cached entries");
    }

    private sealed class CacheSettings
    {
        public TimeSpan Duration { get; init; }
        public bool SlidingExpiration { get; init; }
    }

    /// <summary>
    /// Wrapper that preserves the concrete .NET type across HybridCache L2 serialization.
    /// When HybridCache stores this in Redis via System.Text.Json, the <see cref="TypeName"/>
    /// field lets us deserialize the <see cref="Value"/> back to the correct type on cache hits.
    /// </summary>
    private sealed class CacheEnvelope
    {
        public string? TypeName { get; set; }
        public JsonElement? Value { get; set; }

        public static CacheEnvelope Wrap(object? value)
        {
            if (value is null)
                return new CacheEnvelope();

            return new CacheEnvelope
            {
                TypeName = value.GetType().AssemblyQualifiedName,
                Value = JsonSerializer.SerializeToElement(value, value.GetType(), JsonOptions)
            };
        }

        public object? Unwrap()
        {
            if (TypeName is null || Value is null)
                return null;

            var type = Type.GetType(TypeName);
            return type is not null
                ? Value.Value.Deserialize(type, JsonOptions)
                : null;
        }
    }
}
