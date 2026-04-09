using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using StackExchange.Redis;

namespace Foundatio.Mediator.Distributed.Redis.Tests;

/// <summary>
/// Aspire fixture that manages a Redis container for integration tests.
/// The container is started once and shared across all tests in the collection.
/// </summary>
public class RedisFixture : IAsyncLifetime
{
    public IConnectionMultiplexer Connection { get; private set; } = null!;
    public DistributedApplication App { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var builder = DistributedApplicationTestingBuilder.Create();

        builder.AddRedis("redis");

        App = await builder.BuildAsync();
        await App.StartAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await App.ResourceNotifications.WaitForResourceHealthyAsync("redis", cts.Token);

        var connectionString = await App.GetConnectionStringAsync("redis", cts.Token);
        Connection = await ConnectionMultiplexer.ConnectAsync(connectionString!);
    }

    public async ValueTask DisposeAsync()
    {
        if (Connection is not null)
            await Connection.DisposeAsync();

        if (App is not null)
            await App.DisposeAsync();
    }
}
