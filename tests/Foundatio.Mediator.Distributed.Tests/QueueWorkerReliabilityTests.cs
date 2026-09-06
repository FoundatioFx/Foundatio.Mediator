using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Foundatio.Mediator.Distributed.Tests;

public class QueueWorkerReliabilityTests
{
    [Fact]
    public async Task Prefetch_ReceivesOnlyAvailableCapacity()
    {
        var time = new FakeTimeProvider();
        await using var client = new ObservedQueueClient(time);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int executions = 0;
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider, client, async (_, _, _, ct, _, _) =>
        {
            Interlocked.Increment(ref executions);
            entered.TrySetResult();
            await release.Task.WaitAsync(ct);
            return null;
        }, time: time, prefetch: 10);
        await SendAsync(client, 3);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            for (int i = 0; i < 3; i++)
            {
                time.Advance(TimeSpan.FromSeconds(16));
                await client.Renewed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            }
            Assert.Equal(1, executions);
            Assert.Equal(1, client.Inner.GetInFlightCount("work"));
            Assert.Equal(2, client.Inner.GetPendingCount("work"));
            Assert.All(client.ReceiveBatchSizes, size => Assert.Equal(1, size));
        }
        finally
        {
            release.TrySetResult();
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnsettledJob_IsRetryPending(bool explicitlyAbandon)
    {
        await using var client = new ObservedQueueClient();
        var store = new InMemoryQueueJobStateStore();
        await store.SetJobStateAsync(new QueueJobState { JobId = "job", QueueName = "work" }, cancellationToken: TestContext.Current.CancellationToken);
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider, client, async (_, _, context, _, _, _) =>
        {
            if (explicitlyAbandon)
                {
                Assert.True(context!.TryGet<QueueContext>(out var queueContext));
                await queueContext!.AbandonAsync(TimeSpan.FromHours(1), TestContext.Current.CancellationToken);
            }
            return null;
        }, store, autoComplete: explicitlyAbandon);
        await SendAsync(client, jobId: "job");
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var state = await WaitForStateAsync(store, QueueJobStatus.RetryPending);
            Assert.Null(state.CompletedUtc);
            Assert.Equal(1, state.Attempt);
            Assert.Equal(0, (await store.GetCounterStatsAsync("work", cancellationToken: TestContext.Current.CancellationToken)).Totals.GetValueOrDefault("processed"));
            Assert.True(await store.RequestCancellationAsync("job", TestContext.Current.CancellationToken));
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task CancellationBeforeDequeue_PreventsHandlerExecution()
    {
        await using var client = new ObservedQueueClient();
        var store = new InMemoryQueueJobStateStore();
        await store.SetJobStateAsync(new QueueJobState { JobId = "job", QueueName = "work" }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await store.RequestCancellationAsync("job", TestContext.Current.CancellationToken));
        int executions = 0;
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider, client, (_, _, _, _, _, _) =>
        {
            Interlocked.Increment(ref executions);
            return new ValueTask<object?>();
        }, store);
        await SendAsync(client, jobId: "job");
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForStateAsync(store, QueueJobStatus.Cancelled);
            Assert.Equal(0, executions);
            Assert.Equal(0, client.Inner.GetInFlightCount("work"));
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task FinalFailure_DeadLettersImmediatelyWithActualErrorAndAttempt()
    {
        await using var client = new ObservedQueueClient();
        var store = new InMemoryQueueJobStateStore();
        await store.SetJobStateAsync(new QueueJobState { JobId = "job", QueueName = "work" }, cancellationToken: TestContext.Current.CancellationToken);
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider, client, (_, _, _, _, _, _) => throw new InvalidOperationException("payment service offline"), store, maxAttempts: 1);
        await SendAsync(client, jobId: "job");
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForStateAsync(store, QueueJobStatus.Failed);
            var deadLetter = Assert.Single(client.Inner.DrainDeadLetterMessages("work"));
            Assert.Equal("payment service offline", deadLetter.Headers[MessageHeaders.DeadLetterReason]);
            Assert.Equal("1", deadLetter.Headers[MessageHeaders.DeadLetterDequeueCount]);
            Assert.Equal(0, client.Inner.GetDelayedCount("work"));
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task FailedCompletion_DoesNotReportCompleted()
    {
        await using var client = new ObservedQueueClient { FailCompletion = true };
        var store = new InMemoryQueueJobStateStore();
        await store.SetJobStateAsync(new QueueJobState { JobId = "job", QueueName = "work" }, cancellationToken: TestContext.Current.CancellationToken);
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider, client, (_, _, _, _, _, _) => new ValueTask<object?>(), store);
        await SendAsync(client, jobId: "job");
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var state = await WaitForStateAsync(store, QueueJobStatus.RetryPending);
            Assert.Null(state.CompletedUtc);
            Assert.Equal(1, client.Inner.GetInFlightCount("work"));
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediator();
        return services.BuildServiceProvider();
    }

    private static QueueWorker CreateWorker(ServiceProvider provider, IQueueClient client, HandleAsyncDelegate handler,
        IQueueJobStateStore? store = null, TimeProvider? time = null, int prefetch = 1, bool autoComplete = true, int maxAttempts = 3)
        => new(client, provider.GetRequiredService<IServiceScopeFactory>(), new QueueWorkerOptions
        {
            QueueName = "work",
            MessageType = typeof(string),
            Registrations = [new HandlerRegistration(typeof(string).AssemblyQualifiedName!, "test", handler, null, true)],
            PrefetchCount = prefetch,
            TrackProgress = store is not null,
            AutoComplete = autoComplete,
            MaxAttempts = maxAttempts
        }, new DistributedQueueOptions(), NullLogger<QueueWorker>.Instance, stateStore: store, timeProvider: time);

    private static Task SendAsync(IQueueClient client, int count = 1, string? jobId = null)
        => client.SendAsync("work", Enumerable.Range(0, count).Select(i => new QueueEntry
        {
            Body = JsonSerializer.SerializeToUtf8Bytes($"message-{i}"),
            Headers = jobId is null ? null : new Dictionary<string, string> { [MessageHeaders.JobId] = jobId }
        }).ToArray(), TestContext.Current.CancellationToken);

    private static async Task<QueueJobState> WaitForStateAsync(IQueueJobStateStore store, QueueJobStatus status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var state = await store.GetJobStateAsync("job", timeout.Token);
            if (state?.Status == status)
                return state;
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class ObservedQueueClient(TimeProvider? time = null) : IQueueClient
    {
        public InMemoryQueueClient Inner { get; } = new(time);
        public Channel<bool> Renewed { get; } = Channel.CreateUnbounded<bool>();
        public List<int> ReceiveBatchSizes { get; } = [];
        public bool FailCompletion { get; init; }
        public Task SendAsync(string name, IReadOnlyList<QueueEntry> entries, CancellationToken ct = default) => Inner.SendAsync(name, entries, ct);
        public Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string name, int maxCount, TimeSpan? visibility, CancellationToken ct = default)
        {
            ReceiveBatchSizes.Add(maxCount);
            return Inner.ReceiveAsync(name, maxCount, visibility, ct);
        }
        public Task CompleteAsync(QueueMessage message, CancellationToken ct = default)
            => FailCompletion ? Task.FromException(new IOException("transport unavailable")) : Inner.CompleteAsync(message, ct);
        public Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken ct = default) => Inner.AbandonAsync(message, delay, ct);
        public async Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken ct = default)
        {
            await Inner.RenewTimeoutAsync(message, extension, ct);
            Renewed.Writer.TryWrite(true);
        }
        public Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken ct = default) => Inner.DeadLetterAsync(message, reason, ct);
        public ValueTask DisposeAsync() => Inner.DisposeAsync();
    }
}
