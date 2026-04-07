#pragma warning disable xUnit1051
using Amazon.Runtime;
using Amazon.SQS;
using Foundatio.Mediator.Distributed;
using Foundatio.Mediator.Distributed.Aws;
using Foundatio.Mediator.Distributed.Tests;
using Foundatio.Xunit;

namespace Foundatio.Mediator.Distributed.Aws.Tests;

/// <summary>
/// SQS queue client tests running against LocalStack managed by Aspire.
/// The LocalStack container is automatically started and stopped.
/// </summary>
public class SqsQueueClientTests(LocalStackFixture fixture, ITestOutputHelper output)
    : QueueClientTestBase(output), IClassFixture<LocalStackFixture>
{
    protected override string TestQueueName => $"test-{Guid.NewGuid():N}";

    protected override IQueueClient CreateClient()
    {
        var sqsClient = new AmazonSQSClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonSQSConfig { ServiceURL = fixture.ServiceUrl });

        return new SqsQueueClient(sqsClient, new SqsQueueClientOptions
        {
            AutoCreateQueues = true,
            WaitTimeSeconds = 1 // Short poll for faster tests
        });
    }

    // ── SQS-specific tests ─────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_LargeHeaders_RoundTrip()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        // SQS supports up to 10 message attributes
        var headers = new Dictionary<string, string>();
        for (int i = 0; i < 10; i++)
            headers[$"header-{i}"] = $"value-{i}-{new string('x', 100)}";

        await client.SendAsync(queueName, new QueueEntry
        {
            Body = "test"u8.ToArray(),
            Headers = headers
        }, TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 10, TestCancellationToken);
        Assert.Single(messages);

        foreach (var (key, value) in headers)
        {
            Assert.True(messages[0].Headers.ContainsKey(key), $"Missing header: {key}");
            Assert.Equal(value, messages[0].Headers[key]);
        }
    }

    [Fact]
    public async Task AbandonAsync_MakesMessageImmediatelyVisible()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry { Body = "abandon-test"u8.ToArray() }, TestCancellationToken);

        var first = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(first);

        await client.AbandonAsync(first[0], cancellationToken: TestCancellationToken);

        // Message should be immediately visible again (visibility set to 0)
        var second = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(second);
        Assert.Equal("abandon-test"u8.ToArray(), second[0].Body.ToArray());
        Assert.True(second[0].DequeueCount >= 2);
    }

    [Fact]
    public async Task CompleteAsync_DeletesMessage_FromSqs()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry { Body = "delete-test"u8.ToArray() }, TestCancellationToken);

        var msgs = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(msgs);

        await client.CompleteAsync(msgs[0], TestCancellationToken);

        // No more messages
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var remaining = await client.ReceiveAsync(queueName, 10, cts.Token);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task SendBatchAsync_SendsUpToTenMessages()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        // Send exactly 10 (SQS batch limit)
        var entries = Enumerable.Range(0, 10).Select(i => new QueueEntry
        {
            Body = System.Text.Encoding.UTF8.GetBytes($"batch-{i}"),
            Headers = new Dictionary<string, string> { ["index"] = i.ToString() }
        }).ToList();

        await client.SendBatchAsync(queueName, entries, TestCancellationToken);

        var received = new List<QueueMessage>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (received.Count < 10 && !cts.IsCancellationRequested)
        {
            var batch = await client.ReceiveAsync(queueName, 10, cts.Token);
            received.AddRange(batch);
        }

        Assert.Equal(10, received.Count);
    }

    [Fact]
    public async Task SendBatchAsync_MoreThanTen_SplitsIntoBatches()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        // Send 15 messages — should be split into batches of 10 + 5
        var entries = Enumerable.Range(0, 15).Select(i => new QueueEntry
        {
            Body = System.Text.Encoding.UTF8.GetBytes($"big-batch-{i}")
        }).ToList();

        await client.SendBatchAsync(queueName, entries, TestCancellationToken);

        var received = new List<QueueMessage>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (received.Count < 15 && !cts.IsCancellationRequested)
        {
            var batch = await client.ReceiveAsync(queueName, 10, cts.Token);
            received.AddRange(batch);
        }

        Assert.Equal(15, received.Count);
    }

    [Fact]
    public async Task ReceivedMessage_HasSqsMetadata()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry { Body = "metadata-test"u8.ToArray() }, TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(messages);

        var msg = messages[0];
        Assert.False(string.IsNullOrEmpty(msg.Id));
        Assert.Equal(queueName, msg.QueueName);
        Assert.Equal(1, msg.DequeueCount);
        Assert.True(msg.EnqueuedAt > DateTimeOffset.MinValue);
        Assert.True(msg.DequeuedAt > DateTimeOffset.MinValue);

        // NativeMessage should be the SQS Message
        Assert.NotNull(msg.NativeMessage);
        Assert.IsType<Amazon.SQS.Model.Message>(msg.NativeMessage);
    }

    [Fact]
    public async Task RenewTimeoutAsync_ExtendsVisibility()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry { Body = "renew-test"u8.ToArray() }, TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(messages);

        // Extend visibility — should not throw
        await client.RenewTimeoutAsync(messages[0], TimeSpan.FromMinutes(1), TestCancellationToken);

        // Complete so we don't leave the message in the queue
        await client.CompleteAsync(messages[0], TestCancellationToken);
    }

    // ── Dead-letter ────────────────────────────────────────────────────

    [Fact]
    public async Task DeadLetterAsync_SendsMessageToDLQAndCompletesOriginal()
    {
        var client = CreateClient();
        var queueName = TestQueueName;
        var dlqName = $"{queueName}-dead-letter";

        await client.SendAsync(queueName, new QueueEntry
        {
            Body = "poison"u8.ToArray(),
            Headers = new Dictionary<string, string> { ["custom"] = "value" }
        }, TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(messages);

        await client.DeadLetterAsync(messages[0], "Bad format", TestCancellationToken);

        // Original queue should be empty
        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var remaining = await client.ReceiveAsync(queueName, 10, cts1.Token);
        Assert.Empty(remaining);

        // DLQ should have the message with dead-letter headers
        var dlqMessages = await client.ReceiveAsync(dlqName, 10, TestCancellationToken);
        Assert.Single(dlqMessages);
        Assert.Equal("poison"u8.ToArray(), dlqMessages[0].Body.ToArray());

        // Verify dead-letter metadata headers
        Assert.Equal("Bad format", dlqMessages[0].Headers[MessageHeaders.DeadLetterReason]);
        Assert.True(dlqMessages[0].Headers.ContainsKey(MessageHeaders.DeadLetteredAt));
        Assert.Equal(queueName, dlqMessages[0].Headers[MessageHeaders.OriginalQueueName]);

        // Preserve original headers
        Assert.Equal("value", dlqMessages[0].Headers["custom"]);
    }

    [Fact]
    public async Task AbandonAsync_WithDelay_MakesMessageVisibleAfterDelay()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, new QueueEntry { Body = "delay-test"u8.ToArray() }, TestCancellationToken);

        var first = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(first);

        // Abandon with 5 second delay
        await client.AbandonAsync(first[0], TimeSpan.FromSeconds(5), TestCancellationToken);

        // Should NOT be visible immediately (visibility delay is 2s, long poll is 1s)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var immediate = await client.ReceiveAsync(queueName, 1, cts.Token);
        Assert.Empty(immediate);
    }
}
