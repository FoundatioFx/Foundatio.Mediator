using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Distributed.Testing;

/// <summary>
/// One in-memory queue, pub/sub, and lock transport that several service providers can share, so a test
/// can stand up two or more "nodes" in one process and prove cross-node behaviour.
/// </summary>
/// <example>
/// <code>
/// var transport = new InMemoryTransport();
/// var nodeA = new ServiceCollection().AddInMemoryDistributedTransport(transport)...;
/// var nodeB = new ServiceCollection().AddInMemoryDistributedTransport(transport)...;
/// </code>
/// </example>
public sealed class InMemoryTransport : IAsyncDisposable
{
    public InMemoryTransport(TimeProvider? timeProvider = null)
    {
        Queues = new RecordingQueueClient(timeProvider: timeProvider);
        PubSub = new InMemoryPubSubClient();
        Locks = new InMemoryQueueLockProvider(timeProvider);
    }

    /// <summary>
    /// The shared queue client, recording every send.
    /// </summary>
    public RecordingQueueClient Queues { get; }

    /// <summary>
    /// The shared pub/sub client.
    /// </summary>
    public InMemoryPubSubClient PubSub { get; }

    /// <summary>
    /// The shared lock provider.
    /// </summary>
    public InMemoryQueueLockProvider Locks { get; }

    /// <summary>
    /// Waits until every queue has been processed. See <see cref="RecordingQueueClient.DrainAsync"/>.
    /// </summary>
    public Task DrainAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        => Queues.DrainAsync(timeout, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await Queues.DisposeAsync().ConfigureAwait(false);
        await PubSub.DisposeAsync().ConfigureAwait(false);
    }
}

public static class InMemoryTransportServiceExtensions
{
    /// <summary>
    /// Registers the transport's queue client, pub/sub client, and lock provider so this service
    /// collection shares them with every other collection given the same transport.
    /// </summary>
    public static IServiceCollection AddInMemoryDistributedTransport(this IServiceCollection services, InMemoryTransport transport)
    {
        services.AddSingleton<IQueueClient>(transport.Queues);
        services.AddSingleton<IPubSubClient>(transport.PubSub);
        services.AddSingleton<IQueueLockProvider>(transport.Locks);
        return services;
    }
}
