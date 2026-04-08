#pragma warning disable xUnit1051
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SQS;
using Foundatio.Mediator.Distributed;
using Foundatio.Mediator.Distributed.Aws;
using Foundatio.Xunit;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed.Aws.Tests;

/// <summary>
/// SNS+SQS pub/sub client tests running against LocalStack managed by Aspire.
/// </summary>
public class SnsSqsPubSubClientTests(LocalStackFixture fixture, ITestOutputHelper output)
    : TestWithLoggingBase(output), IClassFixture<LocalStackFixture>
{
    private SnsSqsPubSubClient CreateClient(string? hostId = null)
    {
        var credentials = new BasicAWSCredentials("test", "test");

        var snsClient = new AmazonSimpleNotificationServiceClient(
            credentials,
            new AmazonSimpleNotificationServiceConfig { ServiceURL = fixture.ServiceUrl });

        var sqsClient = new AmazonSQSClient(
            credentials,
            new AmazonSQSConfig { ServiceURL = fixture.ServiceUrl });

        var options = new SnsSqsPubSubClientOptions
        {
            AutoCreate = true,
            WaitTimeSeconds = 1,
            CleanupOnDispose = true
        };

        var notificationOptions = new DistributedNotificationOptions
        {
            HostId = hostId ?? Guid.NewGuid().ToString("N"),
            Topic = $"test-topic-{Guid.NewGuid():N}"
        };

        return new SnsSqsPubSubClient(
            snsClient,
            sqsClient,
            options,
            notificationOptions,
            Log.CreateLogger<SnsSqsPubSubClient>());
    }

    [Fact]
    public async Task PublishAsync_WithNoSubscribers_DoesNotThrow()
    {
        await using var client = CreateClient();

        await client.PublishAsync("no-sub-topic", new PubSubEntry { Body = "hello"u8.ToArray() }, TestCancellationToken);
    }

    [Fact]
    public async Task SubscribeAsync_ReceivesPublishedMessage()
    {
        await using var client = CreateClient();
        var topic = $"test-{Guid.NewGuid():N}";

        PubSubMessage? received = null;
        var signal = new SemaphoreSlim(0);

        await using var sub = await client.SubscribeAsync(topic, (msg, ct) =>
        {
            received = msg;
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        var headers = new Dictionary<string, string> { ["key"] = "value" };
        await client.PublishAsync(topic, new PubSubEntry { Body = "hello"u8.ToArray(), Headers = headers }, TestCancellationToken);

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(30)),
            "Timed out waiting for message");
        Assert.NotNull(received);
        Assert.Equal("hello"u8.ToArray(), received.Body.ToArray());
        Assert.Equal("value", received.Headers["key"]);
    }

    [Fact]
    public async Task SubscribeAsync_MultipleMessages_AllReceived()
    {
        await using var client = CreateClient();
        var topic = $"test-{Guid.NewGuid():N}";

        var received = new List<string>();
        var signal = new SemaphoreSlim(0);

        await using var sub = await client.SubscribeAsync(topic, (msg, ct) =>
        {
            lock (received)
                received.Add(System.Text.Encoding.UTF8.GetString(msg.Body.Span));
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        for (int i = 0; i < 3; i++)
            await client.PublishAsync(topic, new PubSubEntry { Body = System.Text.Encoding.UTF8.GetBytes($"msg-{i}") }, TestCancellationToken);

        for (int i = 0; i < 3; i++)
            Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(30)), $"Timed out waiting for message {i}");

        Assert.Equal(3, received.Count);
        for (int i = 0; i < 3; i++)
            Assert.Contains($"msg-{i}", received);
    }

    [Fact]
    public async Task SubscribeAsync_HeadersRoundTrip()
    {
        await using var client = CreateClient();
        var topic = $"test-{Guid.NewGuid():N}";

        PubSubMessage? received = null;
        var signal = new SemaphoreSlim(0);

        await using var sub = await client.SubscribeAsync(topic, (msg, ct) =>
        {
            received = msg;
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        var headers = new Dictionary<string, string>
        {
            ["h1"] = "v1",
            ["h2"] = "v2",
            ["h3"] = "v3"
        };
        await client.PublishAsync(topic, new PubSubEntry { Body = "test"u8.ToArray(), Headers = headers }, TestCancellationToken);

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.NotNull(received);
        Assert.Equal("v1", received.Headers["h1"]);
        Assert.Equal("v2", received.Headers["h2"]);
        Assert.Equal("v3", received.Headers["h3"]);
    }

    [Fact]
    public async Task DisposeSubscription_StopsReceiving()
    {
        await using var client = CreateClient();
        var topic = $"test-{Guid.NewGuid():N}";

        int count = 0;
        var signal = new SemaphoreSlim(0);

        var sub = await client.SubscribeAsync(topic, (msg, ct) =>
        {
            Interlocked.Increment(ref count);
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        await client.PublishAsync(topic, new PubSubEntry { Body = "msg1"u8.ToArray() }, TestCancellationToken);
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(1, count);

        // Dispose subscription
        await sub.DisposeAsync();

        await client.PublishAsync(topic, new PubSubEntry { Body = "msg2"u8.ToArray() }, TestCancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(3), TestCancellationToken);

        Assert.Equal(1, count); // Should not have received msg2
    }

    [Fact]
    public async Task PublishAsync_NoHeaders_ReceivesEmptyHeaders()
    {
        await using var client = CreateClient();
        var topic = $"test-{Guid.NewGuid():N}";

        PubSubMessage? received = null;
        var signal = new SemaphoreSlim(0);

        await using var sub = await client.SubscribeAsync(topic, (msg, ct) =>
        {
            received = msg;
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        await client.PublishAsync(topic, new PubSubEntry { Body = "no-headers"u8.ToArray() }, TestCancellationToken);

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.NotNull(received);
        Assert.Equal("no-headers"u8.ToArray(), received.Body.ToArray());
        Assert.Empty(received.Headers);
    }
}
