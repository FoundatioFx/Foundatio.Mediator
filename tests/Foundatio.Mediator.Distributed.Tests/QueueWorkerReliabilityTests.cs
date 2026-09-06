using System.Text.Json;
using Foundatio.Mediator.Distributed.Testing;
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

    [Fact]
    public async Task ManualCompleteThenThrow_RemainsCompleted()
    {
        await using var client = new ObservedQueueClient();
        var store = new InMemoryQueueJobStateStore();
        await store.SetJobStateAsync(new QueueJobState { JobId = "job", QueueName = "work" }, cancellationToken: TestContext.Current.CancellationToken);
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider, client, async (_, _, context, ct, _, _) =>
        {
            context!.TryGet<QueueContext>(out var queue);
            await queue!.CompleteAsync(ct);
            throw new InvalidOperationException("Failure after explicit settlement");
        }, store);
        await SendAsync(client, jobId: "job");
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForStateAsync(store, QueueJobStatus.Completed);
            Assert.Equal(0, client.Inner.GetInFlightCount("work"));
            Assert.Equal(0, client.Inner.GetDeadLetterCount("work"));
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task KnownLeaseLoss_CancelsCooperativeHandler()
    {
        var time = new FakeTimeProvider();
        await using var client = new ObservedQueueClient(time) { LoseLeaseOnRenew = true };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider, client, async (_, _, _, ct, _, _) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { if (ct.IsCancellationRequested) cancelled.TrySetResult(); }
            return null;
        }, time: time);
        await SendAsync(client);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            time.Advance(TimeSpan.FromSeconds(16));
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { await worker.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Shutdown_ReceiveIgnoresCancellation_ReturnsAndAbandonsLateDelivery()
    {
        await using var client = new ObservedQueueClient { DelayReceive = true };
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider, client, (_, _, _, _, _, _) => throw new InvalidOperationException("Must not execute late delivery"));
        await SendAsync(client);
        await worker.StartAsync(CancellationToken.None);
        await client.ReceiveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        client.ReleaseReceive.TrySetResult();
        await client.Abandoned.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, client.Inner.GetPendingCount("work"));
        Assert.Equal(0, client.Inner.GetInFlightCount("work"));
    }

    [Fact]
    public async Task Drain_WaitsForDelayedRetryAndWorkAfterManualAcknowledgment()
    {
        var time = new FakeTimeProvider();
        await using var client = new RecordingQueueClient(new InMemoryQueueClient(time));
        var abandoned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider, client, async (_, _, context, ct, _, _) =>
        {
            context!.TryGet<QueueContext>(out var queue);
            if (queue!.DequeueCount == 1)
            {
                await queue.AbandonAsync(TimeSpan.FromHours(1), ct);
                abandoned.TrySetResult();
            }
            else
            {
                await queue.CompleteAsync(ct);
                acknowledged.TrySetResult();
                await release.Task.WaitAsync(ct);
            }
            return null;
        }, time: time);
        await SendAsync(client);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await abandoned.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var delayedDrain = client.DrainAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(delayedDrain.IsCompleted);
            time.Advance(TimeSpan.FromHours(1));
            await acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var settledDrain = client.DrainAsync(cancellationToken: TestContext.Current.CancellationToken);
            Assert.False(settledDrain.IsCompleted);
            release.TrySetResult();
            await Task.WhenAll(delayedDrain, settledDrain).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { release.TrySetResult(); await worker.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task StalledStateStore_DoesNotBlockLeaseRenewal()
    {
        var time = new FakeTimeProvider();
        await using var client = new ObservedQueueClient(time);
        var store = new StalledStartStore();
        await store.SetJobStateAsync(new QueueJobState { JobId = "job", QueueName = "work" }, cancellationToken: TestContext.Current.CancellationToken);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider, client, (_, _, _, _, _, _) => { entered.TrySetResult(); return new((object?)null); }, store, time);
        await SendAsync(client, jobId: "job");
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await store.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            time.Advance(TimeSpan.FromSeconds(16));
            await client.Renewed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(entered.Task.IsCompleted);
            Assert.Equal(1, client.Inner.GetInFlightCount("work"));
            store.Release.TrySetResult();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally { store.Release.TrySetResult(); await worker.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task AcknowledgmentIgnoringCancellation_IsBoundedAndDoesNotClaimCompletion()
    {
        var time = new FakeTimeProvider();
        await using var client = new ObservedQueueClient(time) { DelayCompletion = true };
        var store = new InMemoryQueueJobStateStore();
        await store.SetJobStateAsync(new QueueJobState { JobId = "job", QueueName = "work" }, cancellationToken: TestContext.Current.CancellationToken);
        await using var provider = CreateProvider();
        using var worker = CreateWorker(provider, client, (_, _, _, _, _, _) => new((object?)null), store, time);
        await SendAsync(client, jobId: "job");
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await client.CompletionEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            time.Advance(TimeSpan.FromSeconds(16));
            await client.Renewed.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            time.Advance(TimeSpan.FromSeconds(16));
            await WaitForStateAsync(store, QueueJobStatus.RetryPending);
            Assert.False(client.CompletionFinished.Task.IsCompleted);
            client.ReleaseCompletion.TrySetResult();
            await client.CompletionFinished.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(QueueJobStatus.RetryPending, (await store.GetJobStateAsync("job", TestContext.Current.CancellationToken))!.Status);
        }
        finally { client.ReleaseCompletion.TrySetResult(); await worker.StopAsync(CancellationToken.None); }
    }

    private sealed class StalledStartStore : IQueueJobStateStore
    {
        private readonly InMemoryQueueJobStateStore _inner = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SetJobStateAsync(QueueJobState state, TimeSpan? expiry = null, CancellationToken cancellationToken = default) => _inner.SetJobStateAsync(state, expiry, cancellationToken);
        public Task<QueueJobState?> GetJobStateAsync(string jobId, CancellationToken cancellationToken = default) => _inner.GetJobStateAsync(jobId, cancellationToken);
        public async Task<bool> UpdateJobStatusAsync(string jobId, QueueJobStatus status, DateTimeOffset? startedUtc = null, DateTimeOffset? completedUtc = null, string? errorMessage = null, int? progress = null, int? attempt = null, TimeSpan? expiry = null, CancellationToken cancellationToken = default)
        {
            if (status == QueueJobStatus.Processing) { Started.TrySetResult(); await Release.Task; }
            return await _inner.UpdateJobStatusAsync(jobId, status, startedUtc, completedUtc, errorMessage, progress, attempt, expiry, cancellationToken);
        }
        public Task<bool> RequestCancellationAsync(string jobId, CancellationToken cancellationToken = default) => _inner.RequestCancellationAsync(jobId, cancellationToken);
        public Task<bool> IsCancellationRequestedAsync(string jobId, CancellationToken cancellationToken = default) => _inner.IsCancellationRequestedAsync(jobId, cancellationToken);
        public Task RemoveJobStateAsync(string jobId, CancellationToken cancellationToken = default) => _inner.RemoveJobStateAsync(jobId, cancellationToken);
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
        public bool DelayCompletion { get; init; }
        public TaskCompletionSource CompletionEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCompletion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CompletionFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool LoseLeaseOnRenew { get; init; }
        public bool DelayReceive { get; init; }
        public TaskCompletionSource ReceiveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseReceive { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Abandoned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SendAsync(string name, IReadOnlyList<QueueEntry> entries, CancellationToken ct = default) => Inner.SendAsync(name, entries, ct);
        public async Task<IReadOnlyList<QueueMessage>> ReceiveAsync(string name, int maxCount, TimeSpan? visibility, CancellationToken ct = default)
        {
            ReceiveBatchSizes.Add(maxCount);
            ReceiveEntered.TrySetResult();
            if (DelayReceive) await ReleaseReceive.Task;
            return await Inner.ReceiveAsync(name, maxCount, visibility, DelayReceive ? CancellationToken.None : ct);
        }
        public async Task CompleteAsync(QueueMessage message, CancellationToken ct = default)
        {
            if (FailCompletion) throw new IOException("transport unavailable");
            CompletionEntered.TrySetResult();
            if (DelayCompletion) await ReleaseCompletion.Task;
            await Inner.CompleteAsync(message, DelayCompletion ? CancellationToken.None : ct);
            CompletionFinished.TrySetResult();
        }
        public async Task AbandonAsync(QueueMessage message, TimeSpan delay = default, CancellationToken ct = default)
        {
            await Inner.AbandonAsync(message, delay, ct);
            Abandoned.TrySetResult();
        }
        public async Task RenewTimeoutAsync(QueueMessage message, TimeSpan extension, CancellationToken ct = default)
        {
            if (LoseLeaseOnRenew) throw new QueueLeaseLostException("Lease revoked by transport");
            await Inner.RenewTimeoutAsync(message, extension, ct);
            Renewed.Writer.TryWrite(true);
        }
        public Task DeadLetterAsync(QueueMessage message, string reason, CancellationToken ct = default) => Inner.DeadLetterAsync(message, reason, ct);
        public ValueTask DisposeAsync() => Inner.DisposeAsync();
    }
}
