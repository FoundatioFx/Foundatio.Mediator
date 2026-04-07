using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Foundatio.Mediator.Distributed.Redis;

/// <summary>
/// Extension methods for configuring Redis-backed services on <see cref="IMediatorBuilder"/>.
/// </summary>
public static class RedisBuilderExtensions
{
    /// <summary>
    /// Registers a Redis-backed <see cref="IQueueJobStateStore"/> for tracking queue job state.
    /// Requires an <see cref="IConnectionMultiplexer"/> to be registered in DI.
    /// </summary>
    /// <param name="builder">The mediator builder.</param>
    /// <param name="configure">Optional configuration callback.</param>
    /// <returns>The mediator builder for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddSingleton&lt;IConnectionMultiplexer&gt;(ConnectionMultiplexer.Connect("localhost"));
    /// services.AddMediator()
    ///     .AddDistributedQueues()
    ///     .UseRedisJobState();
    /// </code>
    /// </example>
    public static IMediatorBuilder UseRedisJobState(
        this IMediatorBuilder builder,
        Action<RedisJobStateStoreOptions>? configure = null)
    {
        var services = builder.Services;
        var options = new RedisJobStateStoreOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<IQueueJobStateStore>(sp =>
            new RedisQueueJobStateStore(
                sp.GetRequiredService<IConnectionMultiplexer>(),
                sp.GetService<RedisJobStateStoreOptions>()));

        return builder;
    }
}
