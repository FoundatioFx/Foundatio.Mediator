using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Foundatio.Mediator.Distributed.Redis;

/// <summary>
/// Extension methods for registering the Redis-backed queue job state store.
/// </summary>
public static class RedisServiceExtensions
{
    /// <summary>
    /// Registers a Redis-backed <see cref="IQueueJobStateStore"/> for tracking queue job state.
    /// Requires an <see cref="IConnectionMultiplexer"/> to be registered in DI.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddSingleton&lt;IConnectionMultiplexer&gt;(ConnectionMultiplexer.Connect("localhost"));
    /// services.AddMediatorRedisJobStateStore();
    /// </code>
    /// </example>
    public static IServiceCollection AddMediatorRedisJobStateStore(
        this IServiceCollection services,
        Action<RedisJobStateStoreOptions>? configure = null)
    {
        var options = new RedisJobStateStoreOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IQueueJobStateStore>(sp =>
            new RedisQueueJobStateStore(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetService<RedisJobStateStoreOptions>()));

        return services;
    }
}
