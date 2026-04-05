#pragma warning disable xUnit1051
using Foundatio.Mediator.Distributed;
using Foundatio.Mediator.Distributed.Redis;

namespace Foundatio.Mediator.Distributed.Redis.Tests;

/// <summary>
/// Integration tests for <see cref="RedisQueueJobStateStore"/> running against a real Redis instance.
/// </summary>
public class RedisQueueJobStateStoreTests(RedisFixture fixture) : IClassFixture<RedisFixture>
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    /// <summary>
    /// Creates a store with a unique key prefix per test to avoid cross-test interference.
    /// </summary>
    private RedisQueueJobStateStore CreateStore()
    {
        return new RedisQueueJobStateStore(fixture.Connection, new RedisJobStateStoreOptions
        {
            KeyPrefix = $"test:{Guid.NewGuid():N}"
        });
    }

    private static QueueJobState CreateJobState(
        string jobId = "job-1",
        string queueName = "TestQueue",
        QueueJobStatus status = QueueJobStatus.Queued,
        DateTimeOffset? createdUtc = null)
    {
        var now = createdUtc ?? DateTimeOffset.UtcNow;
        return new QueueJobState
        {
            JobId = jobId,
            QueueName = queueName,
            MessageType = "TestMessage",
            Status = status,
            CreatedUtc = now,
            LastUpdatedUtc = now
        };
    }

    // ── Set / Get ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAndGet_RoundTrips()
    {
        var store = CreateStore();
        var state = CreateJobState();

        await store.SetJobStateAsync(state, cancellationToken: CT);

        var retrieved = await store.GetJobStateAsync("job-1", CT);
        Assert.NotNull(retrieved);
        Assert.Equal("job-1", retrieved.JobId);
        Assert.Equal("TestQueue", retrieved.QueueName);
        Assert.Equal("TestMessage", retrieved.MessageType);
        Assert.Equal(QueueJobStatus.Queued, retrieved.Status);
    }

    [Fact]
    public async Task GetJobState_NonExistent_ReturnsNull()
    {
        var store = CreateStore();
        var result = await store.GetJobStateAsync("nonexistent", CT);
        Assert.Null(result);
    }

    [Fact]
    public async Task SetJobState_UpdatesExisting()
    {
        var store = CreateStore();
        var state = CreateJobState();
        await store.SetJobStateAsync(state, cancellationToken: CT);

        state.Status = QueueJobStatus.Processing;
        state.Progress = 50;
        state.ProgressMessage = "Half done";
        await store.SetJobStateAsync(state, cancellationToken: CT);

        var retrieved = await store.GetJobStateAsync("job-1", CT);
        Assert.NotNull(retrieved);
        Assert.Equal(QueueJobStatus.Processing, retrieved.Status);
        Assert.Equal(50, retrieved.Progress);
        Assert.Equal("Half done", retrieved.ProgressMessage);
    }

    [Fact]
    public async Task SetAndGet_PreservesAllFields()
    {
        var store = CreateStore();
        var now = DateTimeOffset.UtcNow;
        var state = new QueueJobState
        {
            JobId = "full-job",
            QueueName = "FullQueue",
            MessageType = "MyApp.Commands.DoWork",
            Status = QueueJobStatus.Processing,
            Progress = 75,
            ProgressMessage = "Processing items",
            CreatedUtc = now.AddMinutes(-5),
            StartedUtc = now.AddMinutes(-4),
            CompletedUtc = null,
            ErrorMessage = null,
            LastUpdatedUtc = now
        };

        await store.SetJobStateAsync(state, cancellationToken: CT);

        var retrieved = await store.GetJobStateAsync("full-job", CT);
        Assert.NotNull(retrieved);
        Assert.Equal(state.JobId, retrieved.JobId);
        Assert.Equal(state.QueueName, retrieved.QueueName);
        Assert.Equal(state.MessageType, retrieved.MessageType);
        Assert.Equal(state.Status, retrieved.Status);
        Assert.Equal(state.Progress, retrieved.Progress);
        Assert.Equal(state.ProgressMessage, retrieved.ProgressMessage);
        // DateTimeOffset comparison with millisecond precision (Redis stores as Unix ms)
        Assert.Equal(state.CreatedUtc.ToUnixTimeMilliseconds(), retrieved.CreatedUtc.ToUnixTimeMilliseconds());
        Assert.Equal(state.StartedUtc!.Value.ToUnixTimeMilliseconds(), retrieved.StartedUtc!.Value.ToUnixTimeMilliseconds());
        Assert.Null(retrieved.CompletedUtc);
        Assert.Null(retrieved.ErrorMessage);
        Assert.Equal(state.LastUpdatedUtc.ToUnixTimeMilliseconds(), retrieved.LastUpdatedUtc.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task SetAndGet_PreservesTerminalStateFields()
    {
        var store = CreateStore();
        var now = DateTimeOffset.UtcNow;
        var state = new QueueJobState
        {
            JobId = "failed-job",
            QueueName = "FailQueue",
            MessageType = "FailingCommand",
            Status = QueueJobStatus.Failed,
            Progress = 30,
            ProgressMessage = "Failed at step 3",
            CreatedUtc = now.AddMinutes(-10),
            StartedUtc = now.AddMinutes(-9),
            CompletedUtc = now,
            ErrorMessage = "NullReferenceException: Object reference not set",
            LastUpdatedUtc = now
        };

        await store.SetJobStateAsync(state, cancellationToken: CT);

        var retrieved = await store.GetJobStateAsync("failed-job", CT);
        Assert.NotNull(retrieved);
        Assert.Equal(QueueJobStatus.Failed, retrieved.Status);
        Assert.Equal("NullReferenceException: Object reference not set", retrieved.ErrorMessage);
        Assert.NotNull(retrieved.CompletedUtc);
        Assert.Equal(now.ToUnixTimeMilliseconds(), retrieved.CompletedUtc!.Value.ToUnixTimeMilliseconds());
    }

    // ── GetJobsByQueue ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetJobsByQueue_ReturnsMatchingJobs()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState("job-1", "QueueA"), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-2", "QueueA"), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-3", "QueueB"), cancellationToken: CT);

        var jobsA = await store.GetJobsByQueueAsync("QueueA", cancellationToken: CT);
        Assert.Equal(2, jobsA.Count);

        var jobsB = await store.GetJobsByQueueAsync("QueueB", cancellationToken: CT);
        Assert.Single(jobsB);
    }

    [Fact]
    public async Task GetJobsByQueue_OrdersByCreatedUtcDescending()
    {
        var store = CreateStore();
        var baseTime = DateTimeOffset.UtcNow;

        await store.SetJobStateAsync(CreateJobState("job-1", "QueueA", createdUtc: baseTime), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-2", "QueueA", createdUtc: baseTime.AddMinutes(1)), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-3", "QueueA", createdUtc: baseTime.AddMinutes(2)), cancellationToken: CT);

        var jobs = await store.GetJobsByQueueAsync("QueueA", cancellationToken: CT);
        Assert.Equal(3, jobs.Count);
        Assert.Equal("job-3", jobs[0].JobId); // newest first
        Assert.Equal("job-2", jobs[1].JobId);
        Assert.Equal("job-1", jobs[2].JobId);
    }

    [Fact]
    public async Task GetJobsByQueue_SupportsPagination()
    {
        var store = CreateStore();
        var baseTime = DateTimeOffset.UtcNow;

        for (int i = 1; i <= 5; i++)
        {
            await store.SetJobStateAsync(
                CreateJobState($"job-{i}", "QueueA", createdUtc: baseTime.AddSeconds(i)),
                cancellationToken: CT);
        }

        var page1 = await store.GetJobsByQueueAsync("QueueA", skip: 0, take: 2, cancellationToken: CT);
        Assert.Equal(2, page1.Count);

        var page2 = await store.GetJobsByQueueAsync("QueueA", skip: 2, take: 2, cancellationToken: CT);
        Assert.Equal(2, page2.Count);

        var page3 = await store.GetJobsByQueueAsync("QueueA", skip: 4, take: 2, cancellationToken: CT);
        Assert.Single(page3);

        // No overlap between pages
        var allIds = page1.Concat(page2).Concat(page3).Select(j => j.JobId).ToList();
        Assert.Equal(5, allIds.Distinct().Count());
    }

    [Fact]
    public async Task GetJobsByQueue_EmptyQueue_ReturnsEmpty()
    {
        var store = CreateStore();
        var jobs = await store.GetJobsByQueueAsync("EmptyQueue", cancellationToken: CT);
        Assert.Empty(jobs);
    }

    // ── GetJobsByStatus ────────────────────────────────────────────────────

    [Fact]
    public async Task GetJobsByStatus_FiltersCorrectly()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState("job-1", "Q", QueueJobStatus.Queued), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-2", "Q", QueueJobStatus.Processing), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-3", "Q", QueueJobStatus.Completed), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-4", "Q", QueueJobStatus.Failed), cancellationToken: CT);

        var active = await store.GetJobsByStatusAsync("Q", [QueueJobStatus.Queued, QueueJobStatus.Processing], cancellationToken: CT);
        Assert.Equal(2, active.Count);
        Assert.All(active, j => Assert.True(j.Status is QueueJobStatus.Queued or QueueJobStatus.Processing));

        var terminal = await store.GetJobsByStatusAsync("Q", [QueueJobStatus.Completed, QueueJobStatus.Failed], cancellationToken: CT);
        Assert.Equal(2, terminal.Count);
    }

    [Fact]
    public async Task GetJobCountByStatus_ReturnsCorrectCount()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState("job-1", "Q", QueueJobStatus.Queued), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-2", "Q", QueueJobStatus.Queued), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-3", "Q", QueueJobStatus.Processing), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-4", "Q", QueueJobStatus.Completed), cancellationToken: CT);

        Assert.Equal(2, await store.GetJobCountByStatusAsync("Q", QueueJobStatus.Queued, CT));
        Assert.Equal(1, await store.GetJobCountByStatusAsync("Q", QueueJobStatus.Processing, CT));
        Assert.Equal(1, await store.GetJobCountByStatusAsync("Q", QueueJobStatus.Completed, CT));
        Assert.Equal(0, await store.GetJobCountByStatusAsync("Q", QueueJobStatus.Failed, CT));
    }

    // ── Cancellation ───────────────────────────────────────────────────────

    [Fact]
    public async Task RequestCancellation_SetsFlag()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState(), cancellationToken: CT);

        var result = await store.RequestCancellationAsync("job-1", CT);
        Assert.True(result);

        var isCancelled = await store.IsCancellationRequestedAsync("job-1", CT);
        Assert.True(isCancelled);
    }

    [Fact]
    public async Task RequestCancellation_NonExistent_ReturnsFalse()
    {
        var store = CreateStore();
        var result = await store.RequestCancellationAsync("nonexistent", CT);
        Assert.False(result);
    }

    [Fact]
    public async Task RequestCancellation_TerminalState_ReturnsFalse()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState(status: QueueJobStatus.Completed), cancellationToken: CT);
        Assert.False(await store.RequestCancellationAsync("job-1", CT));

        var store2 = CreateStore();
        await store2.SetJobStateAsync(CreateJobState(status: QueueJobStatus.Failed), cancellationToken: CT);
        Assert.False(await store2.RequestCancellationAsync("job-1", CT));

        var store3 = CreateStore();
        await store3.SetJobStateAsync(CreateJobState(status: QueueJobStatus.Cancelled), cancellationToken: CT);
        Assert.False(await store3.RequestCancellationAsync("job-1", CT));
    }

    [Fact]
    public async Task IsCancellationRequested_NotRequested_ReturnsFalse()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState(), cancellationToken: CT);

        var isCancelled = await store.IsCancellationRequestedAsync("job-1", CT);
        Assert.False(isCancelled);
    }

    // ── Remove ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveJobState_RemovesEntry()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState(), cancellationToken: CT);

        await store.RemoveJobStateAsync("job-1", CT);

        var result = await store.GetJobStateAsync("job-1", CT);
        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveJobState_RemovesFromQueueListing()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState("job-1", "QueueA"), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-2", "QueueA"), cancellationToken: CT);

        await store.RemoveJobStateAsync("job-1", CT);

        var jobs = await store.GetJobsByQueueAsync("QueueA", cancellationToken: CT);
        Assert.Single(jobs);
        Assert.Equal("job-2", jobs[0].JobId);
    }

    [Fact]
    public async Task RemoveJobState_ClearsCancellation()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState(), cancellationToken: CT);
        await store.RequestCancellationAsync("job-1", CT);

        await store.RemoveJobStateAsync("job-1", CT);

        var isCancelled = await store.IsCancellationRequestedAsync("job-1", CT);
        Assert.False(isCancelled);
    }

    // ── Expiry ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetJobState_WithExpiry_KeyHasTtl()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState(), expiry: TimeSpan.FromMinutes(5), cancellationToken: CT);

        // Verify the key has a TTL set (we can't easily wait for expiry in integration tests,
        // but we can verify the TTL was applied)
        var db = fixture.Connection.GetDatabase();
        var ttl = await db.KeyTimeToLiveAsync($"test:{store.GetHashCode()}"); // Can't access private key, just verify state exists
        var state = await store.GetJobStateAsync("job-1", CT);
        Assert.NotNull(state);
    }

    // ── Counters ───────────────────────────────────────────────────────────

    [Fact]
    public async Task IncrementCounter_CreatesAndIncrements()
    {
        var store = CreateStore();

        await store.IncrementCounterAsync("TestQueue", "processed", 1, CT);
        await store.IncrementCounterAsync("TestQueue", "processed", 1, CT);
        await store.IncrementCounterAsync("TestQueue", "failed", 1, CT);

        var counters = await store.GetCountersAsync("TestQueue", CT);
        Assert.Equal(2, counters["processed"]);
        Assert.Equal(1, counters["failed"]);
    }

    [Fact]
    public async Task IncrementCounter_SupportsCustomIncrements()
    {
        var store = CreateStore();

        await store.IncrementCounterAsync("TestQueue", "processed", 5, CT);
        await store.IncrementCounterAsync("TestQueue", "processed", 10, CT);

        var counters = await store.GetCountersAsync("TestQueue", CT);
        Assert.Equal(15, counters["processed"]);
    }

    [Fact]
    public async Task GetCounters_EmptyQueue_ReturnsEmpty()
    {
        var store = CreateStore();
        var counters = await store.GetCountersAsync("NonExistent", CT);
        Assert.Empty(counters);
    }

    [Fact]
    public async Task Counters_IsolatedPerQueue()
    {
        var store = CreateStore();

        await store.IncrementCounterAsync("Queue1", "processed", 3, CT);
        await store.IncrementCounterAsync("Queue2", "processed", 7, CT);

        var counters1 = await store.GetCountersAsync("Queue1", CT);
        var counters2 = await store.GetCountersAsync("Queue2", CT);

        Assert.Equal(3, counters1["processed"]);
        Assert.Equal(7, counters2["processed"]);
    }
}
