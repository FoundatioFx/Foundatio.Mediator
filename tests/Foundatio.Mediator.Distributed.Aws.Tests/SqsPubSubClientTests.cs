#pragma warning disable xUnit1051
using System.Globalization;
using System.Text;
using Amazon.SQS;
using Amazon.SQS.Model;
using Foundatio.Mediator.Distributed;
using Foundatio.Mediator.Distributed.Aws;
using Foundatio.Xunit;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed.Aws.Tests;

/// <summary>
/// SNS+SQS pub/sub client tests running against the LocalStack container shared by <see cref="LocalStackCollection"/>.
/// </summary>
[Collection(nameof(LocalStackCollection))]
public class SqsPubSubClientTests(LocalStackFixture fixture, ITestOutputHelper output) : TestWithLoggingBase(output)
{
    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private SqsPubSubClient CreateClient(string? hostId = null, Action<SqsPubSubClientOptions>? configure = null, bool receiveNotifications = true)
    {
        var options = new SqsPubSubClientOptions
        {
            AutoCreate = true,
            WaitTimeSeconds = 1,
            CleanupOnDispose = true
        };
        configure?.Invoke(options);

        var notificationOptions = new DistributedNotificationOptions
        {
            HostId = hostId ?? NewId("host"),
            ReceiveNotifications = receiveNotifications,
            Topic = NewId("test-topic")
        };

        return new SqsPubSubClient(
            fixture.CreateSnsClient(),
            fixture.CreateSqsClient(),
            options,
            notificationOptions,
            Log.CreateLogger<SqsPubSubClient>());
    }

    private static async Task<(string Url, string Arn, Dictionary<string, string> Attributes)> GetQueueAsync(IAmazonSQS sqs, string queueName, CancellationToken cancellationToken)
    {
        var url = (await sqs.GetQueueUrlAsync(queueName, cancellationToken)).QueueUrl;
        var attributes = (await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = url, AttributeNames = [QueueAttributeName.All] }, cancellationToken)).Attributes;
        return (url, attributes[QueueAttributeName.QueueArn], attributes);
    }

    [Fact]
    public async Task PublishAsync_WithNoSubscribers_DoesNotThrow()
    {
        await using var client = CreateClient();

        await client.PublishAsync("no-sub-topic", [new PubSubEntry { Body = "hello"u8.ToArray() }], TestCancellationToken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SubscribeAsync_ReceivesPublishedMessage(bool receiveNotifications)
    {
        await using var client = CreateClient(receiveNotifications: receiveNotifications);
        var topic = NewId("test");

        PubSubMessage? received = null;
        using var signal = new SemaphoreSlim(0);

        await using var sub = await client.SubscribeAsync(topic, (msg, ct) =>
        {
            received = msg;
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        var headers = new Dictionary<string, string> { ["key"] = "value" };
        await client.PublishAsync(topic, [new PubSubEntry { Body = "hello"u8.ToArray(), Headers = headers }], TestCancellationToken);

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
        var topic = NewId("test");

        var received = new List<string>();
        using var signal = new SemaphoreSlim(0);

        await using var sub = await client.SubscribeAsync(topic, (msg, ct) =>
        {
            lock (received)
                received.Add(Encoding.UTF8.GetString(msg.Body.Span));
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        // 12 messages exercise the 10-entry PublishBatch chunking
        var entries = Enumerable.Range(0, 12).Select(i => new PubSubEntry { Body = Encoding.UTF8.GetBytes($"msg-{i}") }).ToList();
        await client.PublishAsync(topic, entries, TestCancellationToken);

        for (int i = 0; i < 12; i++)
            Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(30)), $"Timed out waiting for message {i}");

        Assert.Equal(12, received.Count);
        for (int i = 0; i < 12; i++)
            Assert.Contains($"msg-{i}", received);
    }

    [Fact]
    public async Task SubscribeAsync_HeadersRoundTrip()
    {
        await using var client = CreateClient();
        var topic = NewId("test");

        PubSubMessage? received = null;
        using var signal = new SemaphoreSlim(0);

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
        await client.PublishAsync(topic, [new PubSubEntry { Body = "test"u8.ToArray(), Headers = headers }], TestCancellationToken);

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
        var topic = NewId("test");

        int count = 0;
        using var signal = new SemaphoreSlim(0);

        var sub = await client.SubscribeAsync(topic, (msg, ct) =>
        {
            Interlocked.Increment(ref count);
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        await client.PublishAsync(topic, [new PubSubEntry { Body = "msg1"u8.ToArray() }], TestCancellationToken);
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(1, count);

        await sub.DisposeAsync();
        await sub.DisposeAsync(); // idempotent

        await client.PublishAsync(topic, [new PubSubEntry { Body = "msg2"u8.ToArray() }], TestCancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(3), TestCancellationToken);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PublishAsync_NoHeaders_ReceivesEmptyHeaders()
    {
        await using var client = CreateClient();
        var topic = NewId("test");

        PubSubMessage? received = null;
        using var signal = new SemaphoreSlim(0);

        await using var sub = await client.SubscribeAsync(topic, (msg, ct) =>
        {
            received = msg;
            signal.Release();
            return Task.CompletedTask;
        }, TestCancellationToken);

        await client.PublishAsync(topic, [new PubSubEntry { Body = "no-headers"u8.ToArray() }], TestCancellationToken);

        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.NotNull(received);
        Assert.Equal("no-headers"u8.ToArray(), received.Body.ToArray());
        Assert.Empty(received.Headers);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OriginFiltering_PreservesRemoteAndPackedHeaders(bool filterSelf)
    {
        string hostId = NewId("origin");
        string topic = NewId("origin-topic");
        await using var sender = CreateClient(hostId, o => o.FilterSelfPublications = filterSelf);
        await using var receiver = CreateClient();
        var own = System.Threading.Channels.Channel.CreateUnbounded<PubSubMessage>();
        var remote = new TaskCompletionSource<PubSubMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var selfSubscription = await sender.SubscribeAsync(topic, (m, _) => { own.Writer.TryWrite(m); return Task.CompletedTask; }, TestCancellationToken);
        await using var remoteSubscription = await receiver.SubscribeAsync(topic, (m, _) => { remote.TrySetResult(m); return Task.CompletedTask; }, TestCancellationToken);
        var headers = Enumerable.Range(0, 12).ToDictionary(i => "custom-" + i, i => "value-" + i);
        headers[MessageHeaders.OriginHostId] = hostId;
        headers["quoted"] = "quotes \" and slash \\";
        await sender.PublishAsync(topic, [new PubSubEntry { Body = "event"u8.ToArray(), Headers = headers }], TestCancellationToken);
        var received = await remote.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellationToken);
        Assert.Equal(headers.OrderBy(h => h.Key), received.Headers.OrderBy(h => h.Key));
        if (filterSelf)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => own.Reader.ReadAsync(timeout.Token).AsTask());
        }
        else
            Assert.Equal(headers.OrderBy(h => h.Key), (await own.Reader.ReadAsync(TestCancellationToken)).Headers.OrderBy(h => h.Key));
    }

    [Fact]
    public async Task FilterQuotaExceeded_SubscriptionFallsBackToUnfilteredDelivery()
    {
        using var sns = new QuotaLimitedSns(fixture.ServiceUrl);
        using var sqs = fixture.CreateSqsClient();
        await using var client = new SqsPubSubClient(sns, sqs, new SqsPubSubClientOptions { WaitTimeSeconds = 1 },
            new DistributedNotificationOptions(), Log.CreateLogger<SqsPubSubClient>());
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string topic = NewId("quota");
        await using var subscription = await client.SubscribeAsync(topic, (_, _) => { delivered.TrySetResult(); return Task.CompletedTask; }, TestCancellationToken);
        await client.PublishAsync(topic, [new PubSubEntry { Body = "{}"u8.ToArray() }], TestCancellationToken);
        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestCancellationToken);
        Assert.Equal(1, sns.FilterAttempts);
    }

    private sealed class QuotaLimitedSns(string endpoint) : Amazon.SimpleNotificationService.AmazonSimpleNotificationServiceClient(
        new Amazon.Runtime.BasicAWSCredentials("test", "test"), new Amazon.SimpleNotificationService.AmazonSimpleNotificationServiceConfig { ServiceURL = endpoint })
    {
        public int FilterAttempts { get; private set; }
        public override Task<Amazon.SimpleNotificationService.Model.SubscribeResponse> SubscribeAsync(Amazon.SimpleNotificationService.Model.SubscribeRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Attributes.ContainsKey("FilterPolicy"))
            {
                FilterAttempts++;
                throw new Amazon.SimpleNotificationService.Model.FilterPolicyLimitExceededException("test quota");
            }
            return base.SubscribeAsync(request, cancellationToken);
        }
    }

    // ── Fan-out, policy, heartbeat, payload ──────────────────────────────

    [Fact]
    public async Task TwoLiveHosts_BothReceiveOnePublishedNotification()
    {
        var sqs = fixture.CreateSqsClient();
        var sns = fixture.CreateSnsClient();
        var topic = NewId("fanout");
        var hostAId = NewId("a");
        var hostBId = NewId("b");

        await using var hostA = CreateClient(hostAId, o => o.HeartbeatInterval = TimeSpan.FromSeconds(1));
        await using var hostB = CreateClient(hostBId);

        var receivedA = new List<PubSubMessage>();
        var receivedB = new List<PubSubMessage>();
        using var signalA = new SemaphoreSlim(0);
        using var signalB = new SemaphoreSlim(0);

        await using var subA = await hostA.SubscribeAsync(topic, (msg, _) => { lock (receivedA) receivedA.Add(msg); signalA.Release(); return Task.CompletedTask; }, TestCancellationToken);
        await using var subB = await hostB.SubscribeAsync(topic, (msg, _) => { lock (receivedB) receivedB.Add(msg); signalB.Release(); return Task.CompletedTask; }, TestCancellationToken);

        // SubscribeAsync alone (no EnsureTopicsAsync) leaves a queue policy that names the topic and the fan-out tags
        var topicArn = (await sns.FindTopicAsync(topic)).TopicArn;
        var queueA = await GetQueueAsync(sqs, $"notifications-{hostAId}", TestCancellationToken);
        Assert.Contains(topicArn, queueA.Attributes[QueueAttributeName.Policy]);
        Assert.Equal("300", queueA.Attributes[QueueAttributeName.MessageRetentionPeriod]);

        var tags = (await sqs.ListQueueTagsAsync(new ListQueueTagsRequest { QueueUrl = queueA.Url }, TestCancellationToken)).Tags;
        Assert.Equal("notification-subscription", tags["fm:role"]);
        Assert.Equal(hostAId, tags["fm:host"]);
        var firstHeartbeat = DateTimeOffset.Parse(tags["fm:heartbeat"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        await using var publisher = CreateClient();
        const string json = """{"Event":"created","Id":7}""";
        await publisher.PublishAsync(topic, [new PubSubEntry
        {
            Body = Encoding.UTF8.GetBytes(json),
            Headers = new Dictionary<string, string> { [MessageHeaders.MessageType] = "Created" }
        }], TestCancellationToken);

        Assert.True(await signalA.WaitAsync(TimeSpan.FromSeconds(30)), "Host A never received the notification");
        Assert.True(await signalB.WaitAsync(TimeSpan.FromSeconds(30)), "Host B never received the notification");
        Assert.Equal(json, Encoding.UTF8.GetString(Assert.Single(receivedA).Body.Span));
        Assert.Equal(json, Encoding.UTF8.GetString(Assert.Single(receivedB).Body.Span));
        Assert.Equal("Created", receivedA[0].Headers[MessageHeaders.MessageType]);

        // The heartbeat tag keeps moving while the host is alive
        await Task.Delay(TimeSpan.FromSeconds(2.5), TestCancellationToken);
        tags = (await sqs.ListQueueTagsAsync(new ListQueueTagsRequest { QueueUrl = queueA.Url }, TestCancellationToken)).Tags;
        Assert.True(DateTimeOffset.Parse(tags["fm:heartbeat"], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) > firstHeartbeat, "heartbeat tag was not refreshed");

        // Oversize notifications are rejected before reaching SNS
        var big = new byte[262_145];
        Array.Fill(big, (byte)'a');
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(topic, [new PubSubEntry { Body = big }], TestCancellationToken));
        Assert.Contains(topic, ex.Message);
        Assert.Contains((big.Length + Encoding.UTF8.GetByteCount("fm-exclude-host") + Encoding.UTF8.GetByteCount("none") + Encoding.UTF8.GetByteCount("String")).ToString("N0"), ex.Message);
    }

    [Fact]
    public async Task EnsureTopicsAsync_SweepsQueuesOfCrashedHosts()
    {
        var sqs = fixture.CreateSqsClient();
        var sns = fixture.CreateSnsClient();
        var prefix = NewId("sweep");
        var topic = NewId("sweep-topic");
        var hostAId = NewId("a");
        var hostBId = NewId("b");

        var hostA = CreateClient(hostAId, o => o.QueuePrefix = prefix);
        var subA = await hostA.SubscribeAsync(topic, (_, _) => Task.CompletedTask, TestCancellationToken);

        var queueA = await GetQueueAsync(sqs, $"{prefix}-{hostAId}", TestCancellationToken);
        var topicArn = (await sns.FindTopicAsync(topic)).TopicArn;
        Assert.Contains((await sns.ListSubscriptionsByTopicAsync(topicArn, TestCancellationToken)).Subscriptions, s => s.Endpoint == queueA.Arn);

        // Host A "crashes": its heartbeat stops moving. Back-date the tag instead of waiting for StaleSubscriptionAge.
        await sqs.TagQueueAsync(new TagQueueRequest
        {
            QueueUrl = queueA.Url,
            Tags = new Dictionary<string, string> { ["fm:heartbeat"] = DateTimeOffset.UtcNow.AddHours(-1).ToString("O") }
        }, TestCancellationToken);

        await using var hostB = CreateClient(hostBId, o => o.QueuePrefix = prefix);
        await hostB.EnsureTopicsAsync([new TopicDefinition { Name = topic }], TestCancellationToken);

        await Assert.ThrowsAsync<QueueDoesNotExistException>(() => sqs.GetQueueUrlAsync($"{prefix}-{hostAId}", TestCancellationToken));
        var subscriptions = (await sns.ListSubscriptionsByTopicAsync(topicArn, TestCancellationToken)).Subscriptions;
        Assert.DoesNotContain(subscriptions, s => s.Endpoint == queueA.Arn);

        var queueB = await GetQueueAsync(sqs, $"{prefix}-{hostBId}", TestCancellationToken);
        Assert.Contains(subscriptions, s => s.Endpoint == queueB.Arn);

        // The swept host shuts down without errors even though its queue and subscription are gone
        await subA.DisposeAsync();
        await hostA.DisposeAsync();
    }

    [Fact]
    public async Task StableHostId_RestartWithinSixtySeconds_StillSubscribes()
    {
        var sqs = fixture.CreateSqsClient();
        var hostId = NewId("stable");
        var topic = NewId("restart");
        var queueName = $"notifications-{hostId}";

        var first = CreateClient(hostId);
        await using (await first.SubscribeAsync(topic, (_, _) => Task.CompletedTask, TestCancellationToken))
        {
            await GetQueueAsync(sqs, queueName, TestCancellationToken);
        }
        await first.DisposeAsync(); // deletes the queue; SQS blocks re-creation of that name for 60 seconds

        await using var second = CreateClient(hostId);
        using var signal = new SemaphoreSlim(0);
        await using var sub = await second.SubscribeAsync(topic, (_, _) => { signal.Release(); return Task.CompletedTask; }, TestCancellationToken);

        await using var publisher = CreateClient();
        await publisher.PublishAsync(topic, [new PubSubEntry { Body = "after-restart"u8.ToArray() }], TestCancellationToken);
        Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(30)), "restarted host never received the notification");

        var queues = (await sqs.ListQueuesAsync(new ListQueuesRequest { QueueNamePrefix = queueName }, TestCancellationToken)).QueueUrls;
        var fallback = Assert.Single(queues);
        Assert.NotEqual(queueName, fallback[(fallback.LastIndexOf('/') + 1)..]);
    }
}
