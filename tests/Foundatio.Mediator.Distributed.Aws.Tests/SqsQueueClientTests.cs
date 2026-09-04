#pragma warning disable xUnit1051
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Foundatio.Mediator.Distributed;
using Foundatio.Mediator.Distributed.Aws;
using Foundatio.Mediator.Distributed.Tests;
using Foundatio.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Foundatio.Mediator.Distributed.Aws.Tests;

/// <summary>
/// SQS queue client tests running against the LocalStack container shared by <see cref="LocalStackCollection"/>.
/// </summary>
[Collection(nameof(LocalStackCollection))]
public class SqsQueueClientTests(LocalStackFixture fixture, ITestOutputHelper output) : QueueClientTestBase(output)
{
    protected override string TestQueueName => $"test-{Guid.NewGuid():N}";

    protected override IQueueClient CreateClient() => CreateClient(new SqsQueueClientOptions());

    private SqsQueueClient CreateClient(SqsQueueClientOptions options, TimeProvider? timeProvider = null)
    {
        options.WaitTimeSeconds = 1; // short poll for faster tests
        return new SqsQueueClient(fixture.CreateSqsClient(), options, timeProvider, Log.CreateLogger<SqsQueueClient>());
    }

    private static async Task<Dictionary<string, string>> GetAttributesAsync(IAmazonSQS sqs, string queueName, CancellationToken cancellationToken)
    {
        var url = (await sqs.GetQueueUrlAsync(queueName, cancellationToken)).QueueUrl;
        var response = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest { QueueUrl = url, AttributeNames = [QueueAttributeName.All] }, cancellationToken);
        return response.Attributes;
    }

    // ── Provisioning ─────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureQueuesAsync_CreateMode_ProvisionsAttributesAndRedrive()
    {
        var sqs = fixture.CreateSqsClient();
        var queueName = TestQueueName;
        var definition = new QueueDefinition
        {
            Name = queueName,
            VisibilityTimeout = TimeSpan.FromSeconds(45),
            MessageRetention = TimeSpan.FromHours(2),
            MaxAttempts = 3
        };

        // Two nodes starting at the same time must not trip over QueueAlreadyExists
        var clientA = CreateClient(new SqsQueueClientOptions());
        var clientB = CreateClient(new SqsQueueClientOptions());
        await Task.WhenAll(
            clientA.EnsureQueuesAsync([definition], TestCancellationToken),
            clientB.EnsureQueuesAsync([definition], TestCancellationToken));

        var attributes = await GetAttributesAsync(sqs, queueName, TestCancellationToken);
        Assert.Equal("45", attributes[QueueAttributeName.VisibilityTimeout]);
        Assert.Equal("7200", attributes[QueueAttributeName.MessageRetentionPeriod]);

        var dlqAttributes = await GetAttributesAsync(sqs, definition.DeadLetterQueueName, TestCancellationToken);
        Assert.Equal("45", dlqAttributes[QueueAttributeName.VisibilityTimeout]);
        Assert.Equal(((int)TimeSpan.FromDays(14).TotalSeconds).ToString(), dlqAttributes[QueueAttributeName.MessageRetentionPeriod]);

        using var redrive = JsonDocument.Parse(attributes[QueueAttributeName.RedrivePolicy]);
        Assert.Equal(dlqAttributes[QueueAttributeName.QueueArn], redrive.RootElement.GetProperty("deadLetterTargetArn").GetString());
        var maxReceive = redrive.RootElement.GetProperty("maxReceiveCount");
        Assert.Equal(5, maxReceive.ValueKind == JsonValueKind.Number ? maxReceive.GetInt32() : int.Parse(maxReceive.GetString()!));

        // ProvisionAsync updates an existing queue whose definition changed
        await clientA.ProvisionAsync([new QueueDefinition
        {
            Name = queueName,
            VisibilityTimeout = TimeSpan.FromSeconds(60),
            MessageRetention = TimeSpan.FromHours(2),
            MaxAttempts = 3
        }], TestCancellationToken);

        attributes = await GetAttributesAsync(sqs, queueName, TestCancellationToken);
        Assert.Equal("60", attributes[QueueAttributeName.VisibilityTimeout]);

        // Unlimited attempts map to the SQS maximum receive count
        var unlimited = new QueueDefinition { Name = TestQueueName, MaxAttempts = -1 };
        await clientA.EnsureQueuesAsync([unlimited], TestCancellationToken);
        using var unlimitedRedrive = JsonDocument.Parse((await GetAttributesAsync(sqs, unlimited.Name, TestCancellationToken))[QueueAttributeName.RedrivePolicy]);
        var unlimitedCount = unlimitedRedrive.RootElement.GetProperty("maxReceiveCount");
        Assert.Equal(1000, unlimitedCount.ValueKind == JsonValueKind.Number ? unlimitedCount.GetInt32() : int.Parse(unlimitedCount.GetString()!));

        // Definitions SQS cannot honor are rejected before any call is made
        var tooLong = new QueueDefinition { Name = TestQueueName, VisibilityTimeout = TimeSpan.FromHours(13) };
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => clientA.EnsureQueuesAsync([tooLong], TestCancellationToken));
        Assert.Contains(tooLong.Name, ex.Message);
        Assert.Contains("12 hours", ex.Message);
        await Assert.ThrowsAsync<QueueDoesNotExistException>(() => sqs.GetQueueUrlAsync(tooLong.Name, TestCancellationToken));
    }

    [Fact]
    public async Task EnsureQueuesAsync_ValidateAndNoneModes_NeverCreate()
    {
        var sqs = fixture.CreateSqsClient();
        var existingName = TestQueueName;
        var missingName = TestQueueName;

        await sqs.CreateQueueAsync(new CreateQueueRequest
        {
            QueueName = existingName,
            Attributes = new Dictionary<string, string> { [QueueAttributeName.VisibilityTimeout] = "30" }
        }, TestCancellationToken);

        var validate = CreateClient(new SqsQueueClientOptions { Provisioning = SqsProvisioningMode.Validate });
        var mismatched = new QueueDefinition { Name = existingName, VisibilityTimeout = TimeSpan.FromSeconds(45) };
        var missing = new QueueDefinition { Name = missingName, DeadLetterEnabled = false };

        // One exception lists every problem: the missing DLQ, the attribute mismatch, and the missing queue
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => validate.EnsureQueuesAsync([mismatched, missing], TestCancellationToken));
        Assert.Contains(mismatched.DeadLetterQueueName, ex.Message);
        Assert.Contains($"Queue '{existingName}' attribute VisibilityTimeout is '30' but the definition expects '45'", ex.Message);
        Assert.Contains($"Queue '{missingName}' does not exist", ex.Message);

        await Assert.ThrowsAsync<QueueDoesNotExistException>(() => sqs.GetQueueUrlAsync(missingName, TestCancellationToken));
        await Assert.ThrowsAsync<QueueDoesNotExistException>(() => sqs.GetQueueUrlAsync(mismatched.DeadLetterQueueName, TestCancellationToken));

        // Neither Validate nor None creates a queue on first use
        var none = CreateClient(new SqsQueueClientOptions { Provisioning = SqsProvisioningMode.None });
        var entry = new QueueEntry { Body = "{}"u8.ToArray() };
        await Assert.ThrowsAsync<QueueDoesNotExistException>(() => validate.SendAsync(missingName, [entry], TestCancellationToken));
        await Assert.ThrowsAsync<QueueDoesNotExistException>(() => none.SendAsync(missingName, [entry], TestCancellationToken));
        await none.EnsureQueuesAsync([missing], TestCancellationToken);
        await Assert.ThrowsAsync<QueueDoesNotExistException>(() => sqs.GetQueueUrlAsync(missingName, TestCancellationToken));

        // Once the queues are provisioned, Validate passes and None resolves the URL lazily
        var create = CreateClient(new SqsQueueClientOptions());
        await create.EnsureQueuesAsync([mismatched, missing], TestCancellationToken);
        await validate.EnsureQueuesAsync([mismatched, missing], TestCancellationToken);

        await none.SendAsync(missingName, [entry], TestCancellationToken);
        Assert.Single(await none.ReceiveAsync(missingName, 1, TestCancellationToken));
    }

    // ── Visibility ───────────────────────────────────────────────────────

    [Fact]
    public async Task ReceiveAsync_VisibilityTimeout_LocksMessageUntilItLapses()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, [new QueueEntry { Body = "locked"u8.ToArray() }], TestCancellationToken);

        var first = await client.ReceiveAsync(queueName, 1, TimeSpan.FromSeconds(6), TestCancellationToken);
        var sw = Stopwatch.StartNew();
        Assert.Single(first);
        Assert.Equal(1, first[0].DequeueCount);

        using var shortCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var second = await client.ReceiveAsync(queueName, 1, TimeSpan.FromSeconds(6), shortCts.Token);
        Assert.Empty(second);

        var remaining = TimeSpan.FromSeconds(7) - sw.Elapsed;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, TestCancellationToken);

        var third = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(third);
        Assert.Equal(2, third[0].DequeueCount);
        Assert.Equal("locked"u8.ToArray(), third[0].Body.ToArray());
    }

    [Fact]
    public async Task AbandonAsync_WithDelay_MakesMessageVisibleAfterDelay()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, [new QueueEntry { Body = "delay-test"u8.ToArray() }], TestCancellationToken);

        var first = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(first);

        await client.AbandonAsync(first[0], TimeSpan.FromSeconds(5), TestCancellationToken);
        var sw = Stopwatch.StartNew();

        // Invisible during the delay (long poll is 1s)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var immediate = await client.ReceiveAsync(queueName, 1, cts.Token);
        Assert.Empty(immediate);

        var remaining = TimeSpan.FromSeconds(6) - sw.Elapsed;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, TestCancellationToken);

        var later = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(later);
        Assert.Equal("delay-test"u8.ToArray(), later[0].Body.ToArray());
        Assert.True(later[0].DequeueCount >= 2);
    }

    // ── Stats ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetQueueStatsAsync_DeadLetterNegativeCache_ExpiresAfterOneMinute()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var client = CreateClient(new SqsQueueClientOptions(), time);
        var queueName = TestQueueName;

        await client.SendAsync(queueName, [new QueueEntry { Body = "{}"u8.ToArray() }], TestCancellationToken);

        var stats = await client.GetQueueStatsAsync([queueName], TestCancellationToken);
        Assert.Equal(1, stats[0].ActiveCount);
        Assert.Equal(0, stats[0].DeadLetterCount);

        // Another node dead-letters something; this client still trusts its negative cache
        var other = CreateClient(new SqsQueueClientOptions());
        await other.SendAsync(QueueDefinition.DeadLetterQueueNameFor(queueName), [new QueueEntry { Body = "{}"u8.ToArray() }], TestCancellationToken);

        stats = await client.GetQueueStatsAsync([queueName], TestCancellationToken);
        Assert.Equal(0, stats[0].DeadLetterCount);

        time.Advance(TimeSpan.FromSeconds(61));

        stats = await client.GetQueueStatsAsync([queueName], TestCancellationToken);
        Assert.Equal(1, stats[0].DeadLetterCount);
    }

    // ── Payload ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_BodyIsRawJsonText_AndOversizeMessagesAreRejected()
    {
        var sqs = fixture.CreateSqsClient();
        var client = CreateClient();
        var queueName = TestQueueName;
        const string json = """{"Name":"Test","Value":42}""";

        await client.SendAsync(queueName, [new QueueEntry
        {
            Body = Encoding.UTF8.GetBytes(json),
            Headers = new Dictionary<string, string> { [MessageHeaders.MessageType] = "TestMessage" }
        }], TestCancellationToken);

        var url = (await sqs.GetQueueUrlAsync(queueName, TestCancellationToken)).QueueUrl;
        var raw = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
        {
            QueueUrl = url,
            MaxNumberOfMessages = 1,
            WaitTimeSeconds = 5,
            MessageAttributeNames = ["All"]
        }, TestCancellationToken);

        Assert.Single(raw.Messages);
        Assert.Equal(json, raw.Messages[0].Body);
        Assert.Equal("TestMessage", raw.Messages[0].MessageAttributes[MessageHeaders.MessageType].StringValue);

        var big = new byte[262_145];
        Array.Fill(big, (byte)'a');
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(queueName, [new QueueEntry
        {
            Body = big,
            Headers = new Dictionary<string, string> { [MessageHeaders.MessageType] = "BigMessage" }
        }], TestCancellationToken));

        int expectedSize = big.Length + Encoding.UTF8.GetByteCount(MessageHeaders.MessageType) + "BigMessage".Length;
        Assert.Contains(queueName, ex.Message);
        Assert.Contains(expectedSize.ToString("N0"), ex.Message);
        Assert.Contains("BigMessage", ex.Message);
    }

    // ── SQS-specific behavior ────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_LargeHeaders_RoundTrip()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        // SQS supports up to 10 message attributes
        var headers = new Dictionary<string, string>();
        for (int i = 0; i < 10; i++)
            headers[$"header-{i}"] = $"value-{i}-{new string('x', 100)}";

        await client.SendAsync(queueName, [new QueueEntry
        {
            Body = "test"u8.ToArray(),
            Headers = headers
        }], TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 10, TestCancellationToken);
        Assert.Single(messages);

        foreach (var (key, value) in headers)
        {
            Assert.True(messages[0].Headers.ContainsKey(key), $"Missing header: {key}");
            Assert.Equal(value, messages[0].Headers[key]);
        }

        headers["header-10"] = "one too many";
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(queueName, [new QueueEntry { Body = "test"u8.ToArray(), Headers = headers }], TestCancellationToken));
    }

    [Fact]
    public async Task AbandonAsync_MakesMessageImmediatelyVisible()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, [new QueueEntry { Body = "abandon-test"u8.ToArray() }], TestCancellationToken);

        var first = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(first);

        await client.AbandonAsync(first[0], cancellationToken: TestCancellationToken);

        var second = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(second);
        Assert.Equal("abandon-test"u8.ToArray(), second[0].Body.ToArray());
        Assert.True(second[0].DequeueCount >= 2);
    }

    [Fact]
    public async Task SendAsync_Batch_MoreThanTen_SplitsIntoBatches()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        var entries = Enumerable.Range(0, 15).Select(i => new QueueEntry
        {
            Body = Encoding.UTF8.GetBytes($"big-batch-{i}"),
            Headers = new Dictionary<string, string> { ["index"] = i.ToString() }
        }).ToList();

        await client.SendAsync(queueName, entries, TestCancellationToken);

        var received = new List<QueueMessage>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (received.Count < 15 && !cts.IsCancellationRequested)
        {
            var batch = await client.ReceiveAsync(queueName, 10, cts.Token);
            received.AddRange(batch);
        }

        Assert.Equal(15, received.Count);
        Assert.Equal(Enumerable.Range(0, 15).Select(i => i.ToString()).Order(), received.Select(m => m.Headers["index"]).Order());
    }

    [Fact]
    public async Task ReceivedMessage_HasSqsMetadata()
    {
        var client = CreateClient();
        var queueName = TestQueueName;

        await client.SendAsync(queueName, [new QueueEntry { Body = "metadata-test"u8.ToArray() }], TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(messages);

        var msg = messages[0];
        Assert.False(string.IsNullOrEmpty(msg.Id));
        Assert.Equal(queueName, msg.QueueName);
        Assert.Equal(1, msg.DequeueCount);
        Assert.True(msg.EnqueuedAt > DateTimeOffset.MinValue);
        Assert.True(msg.DequeuedAt > DateTimeOffset.MinValue);
        Assert.IsType<Message>(msg.NativeMessage);
    }

    // ── Dead-letter ──────────────────────────────────────────────────────

    [Fact]
    public async Task DeadLetterAsync_SendsMessageToDLQAndCompletesOriginal()
    {
        var client = CreateClient();
        var queueName = TestQueueName;
        var dlqName = QueueDefinition.DeadLetterQueueNameFor(queueName);

        await client.SendAsync(queueName, [new QueueEntry
        {
            Body = "poison"u8.ToArray(),
            Headers = new Dictionary<string, string> { ["custom"] = "value" }
        }], TestCancellationToken);

        var messages = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(messages);

        await client.DeadLetterAsync(messages[0], "Bad format", TestCancellationToken);

        using var cts1 = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var remaining = await client.ReceiveAsync(queueName, 10, cts1.Token);
        Assert.Empty(remaining);

        var dlqMessages = await client.ReceiveDeadLettersAsync(queueName, 10, TestCancellationToken);
        Assert.Single(dlqMessages);
        Assert.Equal(dlqName, dlqMessages[0].QueueName);
        Assert.Equal("poison"u8.ToArray(), dlqMessages[0].Body.ToArray());
        Assert.Equal("Bad format", dlqMessages[0].Headers[MessageHeaders.DeadLetterReason]);
        Assert.True(dlqMessages[0].Headers.ContainsKey(MessageHeaders.DeadLetteredAt));
        Assert.Equal(queueName, dlqMessages[0].Headers[MessageHeaders.OriginalQueueName]);
        Assert.Equal("value", dlqMessages[0].Headers["custom"]);

        // Replay puts it back on the original queue without the dead-letter headers
        await client.ReplayAsync(dlqMessages[0], TestCancellationToken);
        var replayed = await client.ReceiveAsync(queueName, 1, TestCancellationToken);
        Assert.Single(replayed);
        Assert.Equal("poison"u8.ToArray(), replayed[0].Body.ToArray());
        Assert.False(replayed[0].Headers.ContainsKey(MessageHeaders.DeadLetterReason));
        Assert.True(replayed[0].Headers.ContainsKey(MessageHeaders.ReplayedAt));
    }
}
