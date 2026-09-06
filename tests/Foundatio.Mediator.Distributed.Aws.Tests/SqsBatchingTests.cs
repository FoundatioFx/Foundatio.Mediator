using System.Threading.Channels;
using System.Diagnostics;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace Foundatio.Mediator.Distributed.Aws.Tests;

public class SqsBatchingTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConcurrentSends_WaitForBroker_AndMapPartialFailuresToTheirCallers()
    {
        using var sdk = new ControlledSqs();
        await using var client = CreateClient(sdk);
        var sends = Enumerable.Range(0, 10).Select(i => client.SendAsync("queue", [new QueueEntry { Body = new byte[] { (byte)('a' + i) } }], CT)).ToArray();
        var request = await sdk.Sends.Reader.ReadAsync(CT);
        Assert.Equal(10, request.Entries.Count);
        Assert.All(sends, task => Assert.False(task.IsCompleted));
        sdk.SendResponse.TrySetResult(new SendMessageBatchResponse
        {
            Successful = request.Entries.Where(e => e.MessageBody != "c").Select(e => new SendMessageBatchResultEntry { Id = e.Id }).ToList(),
            Failed = [new BatchResultErrorEntry { Id = request.Entries.Single(e => e.MessageBody == "c").Id, Code = "Rejected", Message = "test rejection" }]
        });
        for (int i = 0; i < sends.Length; i++)
        {
            if (i == 2) Assert.Contains("Rejected", (await Assert.ThrowsAsync<InvalidOperationException>(() => sends[i])).Message);
            else await sends[i].WaitAsync(CT);
        }
        Assert.False(sdk.Sends.Reader.TryRead(out _));
    }

    [Fact]
    public async Task CancelOneDispatchedSend_DoesNotCancelItsPeers()
    {
        using var sdk = new ControlledSqs();
        await using var client = CreateClient(sdk);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(CT);
        var sends = Enumerable.Range(0, 10).Select(i => client.SendAsync("queue", [new QueueEntry { Body = "{}"u8.ToArray() }], i == 0 ? cancelled.Token : CT)).ToArray();
        var request = await sdk.Sends.Reader.ReadAsync(CT);
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sends[0]);
        Assert.False(sdk.RequestToken.IsCancellationRequested);
        sdk.SendResponse.TrySetResult(new SendMessageBatchResponse { Successful = request.Entries.Select(e => new SendMessageBatchResultEntry { Id = e.Id }).ToList() });
        await Task.WhenAll(sends.Skip(1)).WaitAsync(CT);
    }

    [Fact]
    public async Task ConcurrentCompletions_WaitForEachAcknowledgment()
    {
        using var sdk = new ControlledSqs();
        await using var client = CreateClient(sdk);
        var completions = Enumerable.Range(0, 10).Select(i => client.CompleteAsync(new QueueMessage
        {
            Id = i.ToString(), QueueName = "queue", Body = "{}"u8.ToArray(), Headers = new Dictionary<string, string>(),
            NativeMessage = new Message { ReceiptHandle = i.ToString() }
        }, CT)).ToArray();
        var request = await sdk.Deletes.Reader.ReadAsync(CT);
        Assert.Equal(10, request.Entries.Count);
        Assert.All(completions, task => Assert.False(task.IsCompleted));
        sdk.DeleteResponse.TrySetResult(new DeleteMessageBatchResponse
        {
            Successful = request.Entries.Where(e => e.ReceiptHandle != "4").Select(e => new DeleteMessageBatchResultEntry { Id = e.Id }).ToList(),
            Failed = [new BatchResultErrorEntry { Id = request.Entries.Single(e => e.ReceiptHandle == "4").Id, Code = "InvalidReceipt", Message = "test receipt" }]
        });
        for (int i = 0; i < completions.Length; i++)
        {
            if (i == 4) await Assert.ThrowsAsync<InvalidOperationException>(() => completions[i]);
            else await completions[i].WaitAsync(CT);
        }
    }

    [Fact]
    public async Task Dispose_WithStalledBrokerAndQueuedSends_CompletesEveryCaller()
    {
        using var sdk = new ControlledSqs();
        var client = CreateClient(sdk);
        var sends = Enumerable.Range(0, 20).Select(_ => client.SendAsync("queue", [new QueueEntry { Body = "{}"u8.ToArray() }], CT)).ToArray();
        await sdk.Sends.Reader.ReadAsync(CT);
        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), CT);
        foreach (var send in sends) await Assert.ThrowsAsync<ObjectDisposedException>(() => send);
        // The fake deliberately ignores request cancellation. Its late completion cannot revive any caller.
        sdk.SendResponse.TrySetResult(new SendMessageBatchResponse());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.SendAsync("queue", [new QueueEntry { Body = "{}"u8.ToArray() }], CT));
    }

    [Fact]
    public async Task MissingBatchResult_FailsOnlyUnconfirmedEntry()
    {
        using var sdk = new ControlledSqs();
        await using var client = CreateClient(sdk);
        var sends = Enumerable.Range(0, 10).Select(_ => client.SendAsync("queue", [new QueueEntry { Body = "{}"u8.ToArray() }], CT)).ToArray();
        var request = await sdk.Sends.Reader.ReadAsync(CT);
        sdk.SendResponse.TrySetResult(new SendMessageBatchResponse { Successful = request.Entries.Skip(1).Select(e => new SendMessageBatchResultEntry { Id = e.Id }).ToList() });
        await Assert.ThrowsAsync<InvalidOperationException>(() => sends[0]);
        await Task.WhenAll(sends.Skip(1)).WaitAsync(CT);
    }

    [Fact]
    public async Task SparseSend_FlushesWithoutWaitingForAFullBatch_AndDoesNotRetainCallerContext()
    {
        using var sdk = new ControlledSqs();
        await using var client = new SqsQueueClient(sdk, new SqsQueueClientOptions { Provisioning = SqsProvisioningMode.None });
        ControlledSqs.Context.Value = "request-tenant";
        try
        {
            var send = client.SendAsync("queue", [new QueueEntry { Body = "{}"u8.ToArray() }], CT);
            var request = await sdk.Sends.Reader.ReadAsync(CT).AsTask().WaitAsync(TimeSpan.FromSeconds(5), CT);
            Assert.Single(request.Entries);
            Assert.Null(sdk.CapturedContext);
            sdk.SendResponse.SetResult(new SendMessageBatchResponse { Successful = [new SendMessageBatchResultEntry { Id = request.Entries[0].Id }] });
            await send.WaitAsync(CT);
            Assert.Equal("request-tenant", ControlledSqs.Context.Value);
        }
        finally { ControlledSqs.Context.Value = null; }
    }

    [Fact]
    public async Task StalledRequest_TimesOutEvenWhenSdkIgnoresCancellation()
    {
        using var sdk = new ControlledSqs();
        await using var client = new SqsQueueClient(sdk, new SqsQueueClientOptions
        {
            Provisioning = SqsProvisioningMode.None,
            Batching = new AwsBatchOptions { MaxDelay = TimeSpan.Zero, RequestTimeout = TimeSpan.FromMilliseconds(50) }
        });
        var send = client.SendAsync("queue", [new QueueEntry { Body = "{}"u8.ToArray() }], CT);
        await sdk.Sends.Reader.ReadAsync(CT);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(5), CT));
        sdk.SendResponse.TrySetResult(new SendMessageBatchResponse());
    }

    [Fact]
    public async Task CancelQueuedSend_RemovesItBeforeDispatch_AndReleasesCapacity()
    {
        using var sdk = new ControlledSqs();
        await using var client = CreateClient(sdk);
        var first = Enumerable.Range(0, 10).Select(_ => client.SendAsync("queue", [new QueueEntry { Body = "{}"u8.ToArray() }], CT)).ToArray();
        var firstRequest = await sdk.Sends.Reader.ReadAsync(CT);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(CT);
        var queued = client.SendAsync("queue", [new QueueEntry { Body = "cancelled"u8.ToArray() }], cancelled.Token);
        var next = Enumerable.Range(0, 10).Select(_ => client.SendAsync("queue", [new QueueEntry { Body = "{}"u8.ToArray() }], CT)).ToArray();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        sdk.SendResponse.SetResult(new SendMessageBatchResponse { Successful = firstRequest.Entries.Select(e => new SendMessageBatchResultEntry { Id = e.Id }).ToList() });
        await Task.WhenAll(first.Concat(next)).WaitAsync(TimeSpan.FromSeconds(5), CT);
        var nextRequest = await sdk.Sends.Reader.ReadAsync(CT);
        Assert.Equal(10, nextRequest.Entries.Count);
        Assert.DoesNotContain(nextRequest.Entries, e => e.MessageBody == "cancelled");
    }

    [Fact]
    public async Task ConcurrentLargeMessages_SplitAtTheAggregateByteLimit()
    {
        using var sdk = new ControlledSqs { AutoComplete = true };
        await using var client = new SqsQueueClient(sdk, new SqsQueueClientOptions
        {
            Provisioning = SqsProvisioningMode.None,
            Batching = new AwsBatchOptions { MaxDelay = TimeSpan.FromMilliseconds(10) }
        });
        var body = System.Text.Encoding.UTF8.GetBytes(new string('a', 80_000));
        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.SendAsync("queue", [new QueueEntry { Body = body }], CT))).WaitAsync(TimeSpan.FromSeconds(5), CT);
        int sent = 0, requests = 0;
        while (sdk.Sends.Reader.TryRead(out var request))
        {
            requests++;
            sent += request.Entries.Count;
            Assert.InRange(request.Entries.Sum(e => System.Text.Encoding.UTF8.GetByteCount(e.MessageBody)), 1, 262_144);
            Assert.Equal(request.Entries.Count, request.Entries.Select(e => e.Id).Distinct().Count());
        }
        Assert.Equal(10, sent);
        Assert.InRange(requests, 4, 10);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HeaderPacking_CanPreserveIndividualNativeAttributes(bool pack)
    {
        using var sdk = new ControlledSqs { AutoComplete = true };
        await using var client = new SqsQueueClient(sdk, new SqsQueueClientOptions { Provisioning = SqsProvisioningMode.None, PackHeaders = pack });
        await client.SendAsync("queue", [new QueueEntry { Body = "{}"u8.ToArray(), Headers = new() { ["first"] = "one", ["second"] = "two" } }], CT);
        var request = await sdk.Sends.Reader.ReadAsync(CT);
        var attributes = Assert.Single(request.Entries).MessageAttributes;
        if (pack)
        {
            Assert.Single(attributes);
            var headers = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(attributes[MessageHeaders.PackedHeaders].StringValue)!;
            Assert.Equal("one", headers["first"]);
            Assert.Equal("two", headers["second"]);
        }
        else
        {
            Assert.Equal(2, attributes.Count);
            Assert.Equal("one", attributes["first"].StringValue);
            Assert.Equal("two", attributes["second"].StringValue);
        }
    }

    [Fact]
    public async Task PayloadLimit_IncludesNativeAttributeDataTypes()
    {
        using var sdk = new ControlledSqs { AutoComplete = true };
        await using var client = new SqsQueueClient(sdk, new SqsQueueClientOptions { Provisioning = SqsProvisioningMode.None });
        var headers = new Dictionary<string, string> { ["a"] = "b" };
        byte[] body = System.Text.Encoding.UTF8.GetBytes(new string('a', 262_144 - 8)); // name + value + String
        await client.SendAsync("queue", [new QueueEntry { Body = body, Headers = headers }], CT);
        Assert.Single((await sdk.Sends.Reader.ReadAsync(CT)).Entries);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("queue", [new QueueEntry { Body = body.Append((byte)'a').ToArray(), Headers = headers }], CT));
        Assert.False(sdk.Sends.Reader.TryRead(out _));
    }

    [Fact]
    public async Task BatchTracing_LinksEachCallerWithoutChoosingOneAsTheParent()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == MediatorActivitySource.Instance.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        using var sdk = new ControlledSqs { AutoComplete = true };
        await using var client = new SqsQueueClient(sdk, new SqsQueueClientOptions
        {
            Provisioning = SqsProvisioningMode.None,
            Batching = new AwsBatchOptions { MaxBatchSize = 2, MaxDelay = TimeSpan.FromSeconds(10) }
        });
        Task first, second;
        ActivityContext firstContext, secondContext;
        using (var caller = new Activity("first").Start())
        {
            firstContext = caller.Context;
            first = client.SendAsync("queue", [new QueueEntry { Body = "{}"u8.ToArray() }], CT);
        }
        using (var caller = new Activity("second").Start())
        {
            secondContext = caller.Context;
            second = client.SendAsync("queue", [new QueueEntry { Body = "{}"u8.ToArray() }], CT);
        }
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5), CT);
        Assert.NotNull(sdk.BatchActivity);
        Assert.Null(sdk.BatchActivity.ParentId);
        Assert.Equal(new[] { firstContext, secondContext }, sdk.BatchActivity.Links.Select(link => link.Context));
    }

    private static SqsQueueClient CreateClient(ControlledSqs sdk) => new(sdk, new SqsQueueClientOptions
    {
        Provisioning = SqsProvisioningMode.None,
        Batching = new AwsBatchOptions { MaxDelay = TimeSpan.FromSeconds(10), MaxConcurrency = 1, Capacity = 10 }
    });

    private sealed class ControlledSqs() : AmazonSQSClient(new BasicAWSCredentials("test", "test"), new AmazonSQSConfig { ServiceURL = "http://localhost:1" })
    {
        public Channel<SendMessageBatchRequest> Sends { get; } = Channel.CreateUnbounded<SendMessageBatchRequest>();
        public Channel<DeleteMessageBatchRequest> Deletes { get; } = Channel.CreateUnbounded<DeleteMessageBatchRequest>();
        public TaskCompletionSource<SendMessageBatchResponse> SendResponse { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<DeleteMessageBatchResponse> DeleteResponse { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken RequestToken { get; private set; }
        public static AsyncLocal<string?> Context { get; } = new();
        public string? CapturedContext { get; private set; }
        public bool AutoComplete { get; init; }
        public Activity? BatchActivity { get; private set; }
        public override Task<GetQueueUrlResponse> GetQueueUrlAsync(GetQueueUrlRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new GetQueueUrlResponse { QueueUrl = "http://localhost:1/" + request.QueueName });
        public override Task<SendMessageBatchResponse> SendMessageBatchAsync(SendMessageBatchRequest request, CancellationToken cancellationToken = default)
        {
            RequestToken = cancellationToken;
            CapturedContext = Context.Value;
            BatchActivity = Activity.Current;
            Sends.Writer.TryWrite(request);
            return AutoComplete
                ? Task.FromResult(new SendMessageBatchResponse { Successful = request.Entries.Select(e => new SendMessageBatchResultEntry { Id = e.Id }).ToList() })
                : SendResponse.Task;
        }
        public override Task<DeleteMessageBatchResponse> DeleteMessageBatchAsync(DeleteMessageBatchRequest request, CancellationToken cancellationToken = default)
        {
            Deletes.Writer.TryWrite(request);
            return DeleteResponse.Task;
        }
    }
}
