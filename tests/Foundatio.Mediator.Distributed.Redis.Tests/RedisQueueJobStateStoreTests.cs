#pragma warning disable xUnit1051
using Foundatio.Mediator.Distributed;
using Foundatio.Mediator.Distributed.Redis;
using StackExchange.Redis;

namespace Foundatio.Mediator.Distributed.Redis.Tests;

/// <summary>
/// Integration tests for <see cref="RedisQueueJobStateStore"/> running against a real Redis instance.
/// </summary>
public class RedisQueueJobStateStoreTests(RedisFixture fixture) : IClassFixture<RedisFixture>
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    private IDatabase Db => fixture.Connection.GetDatabase();

    private RedisQueueJobStateStore CreateStore() => CreateStore(out _);

    /// <summary>
    /// Creates a store with a unique key prefix per test to avoid cross-test interference.
    /// </summary>
    private RedisQueueJobStateStore CreateStore(out string keyPrefix, Action<RedisJobStateStoreOptions>? configure = null, TimeProvider? timeProvider = null)
    {
        var options = new RedisJobStateStoreOptions { KeyPrefix = $"test:{Guid.NewGuid():N}" };
        configure?.Invoke(options);
        keyPrefix = options.KeyPrefix;
        return new RedisQueueJobStateStore(fixture.Connection, options, timeProvider);
    }

    private static string JobKey(string keyPrefix, string jobId) => $"{keyPrefix}:{jobId}";
    private static string CancelKey(string keyPrefix, string jobId) => $"{keyPrefix}:{jobId}:cancel";
    private static string StatusSetKey(string keyPrefix, string queueName, QueueJobStatus status) => $"{keyPrefix}:queues:{queueName}:status:{(int)status}";

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
        Assert.Null(retrieved.Metadata);
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
        var store = CreateStore(out var prefix);
        var state = CreateJobState();
        await store.SetJobStateAsync(state, cancellationToken: CT);

        var updated = state with { Status = QueueJobStatus.Processing, Progress = 50, ProgressMessage = "Half done" };
        await store.SetJobStateAsync(updated, cancellationToken: CT);

        var retrieved = await store.GetJobStateAsync("job-1", CT);
        Assert.NotNull(retrieved);
        Assert.Equal(QueueJobStatus.Processing, retrieved.Status);
        Assert.Equal(50, retrieved.Progress);
        Assert.Equal("Half done", retrieved.ProgressMessage);

        // Replacing a job moves it between status sets instead of leaving it in both
        Assert.Equal(0, await Db.SortedSetLengthAsync(StatusSetKey(prefix, "TestQueue", QueueJobStatus.Queued)));
        Assert.Equal(1, await Db.SortedSetLengthAsync(StatusSetKey(prefix, "TestQueue", QueueJobStatus.Processing)));
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
            Attempt = 2,
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
        Assert.Equal(state.Attempt, retrieved.Attempt);
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

    [Fact]
    public async Task Metadata_RoundTrips_AndSurvivesStatusUpdate()
    {
        var store = CreateStore();
        var state = CreateJobState() with
        {
            Metadata = new Dictionary<string, string> { ["tenant"] = "acme", ["user"] = "42" }
        };

        await store.SetJobStateAsync(state, cancellationToken: CT);

        var retrieved = await store.GetJobStateAsync("job-1", CT);
        Assert.NotNull(retrieved?.Metadata);
        Assert.Equal(2, retrieved.Metadata.Count);
        Assert.Equal("acme", retrieved.Metadata["tenant"]);
        Assert.Equal("42", retrieved.Metadata["user"]);

        await store.UpdateJobStatusAsync("job-1", QueueJobStatus.Processing, startedUtc: DateTimeOffset.UtcNow, cancellationToken: CT);
        await store.UpdateJobProgressAsync("job-1", 50, "half", cancellationToken: CT);

        retrieved = await store.GetJobStateAsync("job-1", CT);
        Assert.NotNull(retrieved?.Metadata);
        Assert.Equal(QueueJobStatus.Processing, retrieved.Status);
        Assert.Equal("acme", retrieved.Metadata["tenant"]);
        Assert.Equal("42", retrieved.Metadata["user"]);

        var listed = await store.GetJobsByStatusAsync("TestQueue", QueueJobStatus.Processing, cancellationToken: CT);
        Assert.Single(listed);
        Assert.Equal("acme", listed[0].Metadata?["tenant"]);

        // Replacing the job drops metadata from the previous version
        await store.SetJobStateAsync(state with { Metadata = new Dictionary<string, string> { ["tenant"] = "globex" } }, cancellationToken: CT);
        retrieved = await store.GetJobStateAsync("job-1", CT);
        Assert.NotNull(retrieved?.Metadata);
        Assert.Single(retrieved.Metadata);
        Assert.Equal("globex", retrieved.Metadata["tenant"]);
    }

    // ── UpdateJobStatus / UpdateJobProgress / Heartbeat ────────────────────

    [Fact]
    public async Task UpdateJobStatus_UpdatesEachField_AndMovesBetweenStatusSets()
    {
        var store = CreateStore();
        var created = DateTimeOffset.UtcNow.AddMinutes(-1);
        await store.SetJobStateAsync(CreateJobState(createdUtc: created), cancellationToken: CT);

        var started = DateTimeOffset.UtcNow;
        await store.UpdateJobStatusAsync("job-1", QueueJobStatus.Processing, startedUtc: started, attempt: 2, progress: 10, cancellationToken: CT);

        var state = await store.GetJobStateAsync("job-1", CT);
        Assert.NotNull(state);
        Assert.Equal(QueueJobStatus.Processing, state.Status);
        Assert.Equal(started.ToUnixTimeMilliseconds(), state.StartedUtc?.ToUnixTimeMilliseconds());
        Assert.Equal(2, state.Attempt);
        Assert.Equal(10, state.Progress);
        Assert.Null(state.CompletedUtc);
        Assert.Null(state.ErrorMessage);
        Assert.True(state.LastUpdatedUtc >= started.AddSeconds(-1));
        Assert.Equal(created.ToUnixTimeMilliseconds(), state.CreatedUtc.ToUnixTimeMilliseconds());

        var completed = DateTimeOffset.UtcNow;
        await store.UpdateJobStatusAsync("job-1", QueueJobStatus.Failed, completedUtc: completed, errorMessage: "boom", progress: 40, cancellationToken: CT);

        state = await store.GetJobStateAsync("job-1", CT);
        Assert.NotNull(state);
        Assert.Equal(QueueJobStatus.Failed, state.Status);
        Assert.Equal(completed.ToUnixTimeMilliseconds(), state.CompletedUtc?.ToUnixTimeMilliseconds());
        Assert.Equal("boom", state.ErrorMessage);
        Assert.Equal(40, state.Progress);
        // Fields not passed are left alone
        Assert.Equal(started.ToUnixTimeMilliseconds(), state.StartedUtc?.ToUnixTimeMilliseconds());
        Assert.Equal(2, state.Attempt);

        Assert.Equal(0, await store.GetJobCountByStatusAsync("TestQueue", QueueJobStatus.Queued, CT));
        Assert.Equal(0, await store.GetJobCountByStatusAsync("TestQueue", QueueJobStatus.Processing, CT));
        Assert.Equal(1, await store.GetJobCountByStatusAsync("TestQueue", QueueJobStatus.Failed, CT));
        var failed = await store.GetJobsByStatusAsync("TestQueue", QueueJobStatus.Failed, cancellationToken: CT);
        Assert.Single(failed);
        Assert.Equal(created.ToUnixTimeMilliseconds(), failed[0].CreatedUtc.ToUnixTimeMilliseconds());

        // Updating a job that does not exist is a no-op and does not create it
        await store.UpdateJobStatusAsync("missing", QueueJobStatus.Completed, cancellationToken: CT);
        Assert.Null(await store.GetJobStateAsync("missing", CT));
    }

    [Fact]
    public async Task UpdateJobStatus_Concurrent_LeavesJobInExactlyOneStatusSet()
    {
        var store = CreateStore(out var prefix);
        await store.SetJobStateAsync(CreateJobState(), cancellationToken: CT);

        var updates = Enumerable.Range(0, 50).Select(i => Task.Run(() => store.UpdateJobStatusAsync(
            "job-1",
            i % 2 == 0 ? QueueJobStatus.Processing : QueueJobStatus.Failed,
            startedUtc: DateTimeOffset.UtcNow,
            attempt: i,
            cancellationToken: CT), CT));

        await Task.WhenAll(updates);

        var state = await store.GetJobStateAsync("job-1", CT);
        Assert.NotNull(state);
        Assert.True(state.Status is QueueJobStatus.Processing or QueueJobStatus.Failed);

        long total = 0;
        foreach (var status in Enum.GetValues<QueueJobStatus>())
        {
            var members = await Db.SortedSetRangeByRankAsync(StatusSetKey(prefix, "TestQueue", status));
            total += members.Length;
            if (members.Length > 0)
                Assert.Equal(state.Status, status);
        }

        Assert.Equal(1, total);
    }

    [Fact]
    public async Task UpdateJobProgress_UpdatesProgressFields()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero));
        var store = CreateStore(out _, timeProvider: clock);
        await store.SetJobStateAsync(CreateJobState(status: QueueJobStatus.Processing, createdUtc: clock.Now), cancellationToken: CT);

        clock.Advance(TimeSpan.FromSeconds(30));
        await store.UpdateJobProgressAsync("job-1", 42, "almost", cancellationToken: CT);

        var state = await store.GetJobStateAsync("job-1", CT);
        Assert.NotNull(state);
        Assert.Equal(42, state.Progress);
        Assert.Equal("almost", state.ProgressMessage);
        Assert.Equal(QueueJobStatus.Processing, state.Status);
        Assert.Equal(clock.Now, state.LastUpdatedUtc);

        // A null message clears the previous one
        await store.UpdateJobProgressAsync("job-1", 43, cancellationToken: CT);
        state = await store.GetJobStateAsync("job-1", CT);
        Assert.Equal(43, state!.Progress);
        Assert.Null(state.ProgressMessage);

        await store.UpdateJobProgressAsync("missing", 10, cancellationToken: CT);
        Assert.Null(await store.GetJobStateAsync("missing", CT));
    }

    [Fact]
    public async Task Heartbeat_BumpsLastUpdatedUtc_AndLeavesStatusUnchanged()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero));
        var store = CreateStore(out var prefix, timeProvider: clock);
        await store.SetJobStateAsync(CreateJobState(status: QueueJobStatus.Processing, createdUtc: clock.Now) with { Progress = 20 }, cancellationToken: CT);

        clock.Advance(TimeSpan.FromMinutes(1));
        await store.HeartbeatAsync("job-1", CT);

        var state = await store.GetJobStateAsync("job-1", CT);
        Assert.NotNull(state);
        Assert.Equal(clock.Now, state.LastUpdatedUtc);
        Assert.Equal(QueueJobStatus.Processing, state.Status);
        Assert.Equal(20, state.Progress);

        var heartbeat = await Db.HashGetAsync(JobKey(prefix, "job-1"), "LastHeartbeatUtc");
        Assert.Equal(clock.Now.ToUnixTimeMilliseconds(), (long)heartbeat);

        Assert.Equal(1, await store.GetJobCountByStatusAsync("TestQueue", QueueJobStatus.Processing, CT));

        // Heartbeat on an unknown job does not create it
        await store.HeartbeatAsync("missing", CT);
        Assert.False(await Db.KeyExistsAsync(JobKey(prefix, "missing")));
    }

    // ── GetJobsByStatus ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetJobsByStatus_ReturnsMatchingJobs()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState("job-1", "QueueA"), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-2", "QueueA"), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-3", "QueueB"), cancellationToken: CT);

        var jobsA = await store.GetJobsByStatusAsync("QueueA", QueueJobStatus.Queued, cancellationToken: CT);
        Assert.Equal(2, jobsA.Count);

        var jobsB = await store.GetJobsByStatusAsync("QueueB", QueueJobStatus.Queued, cancellationToken: CT);
        Assert.Single(jobsB);
    }

    [Fact]
    public async Task GetJobsByStatus_OrdersByCreatedUtcDescending()
    {
        var store = CreateStore();
        var baseTime = DateTimeOffset.UtcNow;

        await store.SetJobStateAsync(CreateJobState("job-1", "QueueA", createdUtc: baseTime), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-2", "QueueA", createdUtc: baseTime.AddMinutes(1)), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-3", "QueueA", createdUtc: baseTime.AddMinutes(2)), cancellationToken: CT);

        var jobs = await store.GetJobsByStatusAsync("QueueA", QueueJobStatus.Queued, cancellationToken: CT);
        Assert.Equal(3, jobs.Count);
        Assert.Equal("job-3", jobs[0].JobId); // newest first
        Assert.Equal("job-2", jobs[1].JobId);
        Assert.Equal("job-1", jobs[2].JobId);
    }

    [Fact]
    public async Task GetJobsByStatus_SupportsPagination()
    {
        var store = CreateStore();
        var baseTime = DateTimeOffset.UtcNow;

        for (int i = 1; i <= 5; i++)
        {
            await store.SetJobStateAsync(
                CreateJobState($"job-{i}", "QueueA", createdUtc: baseTime.AddSeconds(i)),
                cancellationToken: CT);
        }

        var page1 = await store.GetJobsByStatusAsync("QueueA", QueueJobStatus.Queued, skip: 0, take: 2, cancellationToken: CT);
        Assert.Equal(2, page1.Count);

        var page2 = await store.GetJobsByStatusAsync("QueueA", QueueJobStatus.Queued, skip: 2, take: 2, cancellationToken: CT);
        Assert.Equal(2, page2.Count);

        var page3 = await store.GetJobsByStatusAsync("QueueA", QueueJobStatus.Queued, skip: 4, take: 2, cancellationToken: CT);
        Assert.Single(page3);

        // No overlap between pages
        var allIds = page1.Concat(page2).Concat(page3).Select(j => j.JobId).ToList();
        Assert.Equal(5, allIds.Distinct().Count());

        Assert.Empty(await store.GetJobsByStatusAsync("QueueA", QueueJobStatus.Queued, take: 0, cancellationToken: CT));
    }

    [Fact]
    public async Task GetJobsByStatus_EmptyQueue_ReturnsEmpty()
    {
        var store = CreateStore();
        var jobs = await store.GetJobsByStatusAsync("EmptyQueue", QueueJobStatus.Queued, cancellationToken: CT);
        Assert.Empty(jobs);
    }

    [Fact]
    public async Task GetJobsByStatus_FiltersCorrectly()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState("job-1", "Q", QueueJobStatus.Queued), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-2", "Q", QueueJobStatus.Processing), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-3", "Q", QueueJobStatus.Completed), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-4", "Q", QueueJobStatus.Failed), cancellationToken: CT);

        var queued = await store.GetJobsByStatusAsync("Q", QueueJobStatus.Queued, cancellationToken: CT);
        Assert.Single(queued);
        Assert.Equal(QueueJobStatus.Queued, queued[0].Status);

        var processing = await store.GetJobsByStatusAsync("Q", QueueJobStatus.Processing, cancellationToken: CT);
        Assert.Single(processing);
        Assert.Equal(QueueJobStatus.Processing, processing[0].Status);

        var completed = await store.GetJobsByStatusAsync("Q", QueueJobStatus.Completed, cancellationToken: CT);
        Assert.Single(completed);

        var failed = await store.GetJobsByStatusAsync("Q", QueueJobStatus.Failed, cancellationToken: CT);
        Assert.Single(failed);
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

    [Fact]
    public async Task ExpiredJobs_AreDroppedFromListingAndCount()
    {
        var store = CreateStore(out var prefix);
        var baseTime = DateTimeOffset.UtcNow;

        await Task.WhenAll(Enumerable.Range(1, 200).Select(i => store.SetJobStateAsync(
            CreateJobState($"job-{i}", "Q", QueueJobStatus.Completed, baseTime.AddMilliseconds(i)),
            expiry: TimeSpan.FromSeconds(1),
            cancellationToken: CT)));

        Assert.Equal(200, await store.GetJobCountByStatusAsync("Q", QueueJobStatus.Completed, CT));

        await Task.Delay(TimeSpan.FromSeconds(2), CT);

        Assert.False(await Db.KeyExistsAsync(JobKey(prefix, "job-1")));

        var page = await store.GetJobsByStatusAsync("Q", QueueJobStatus.Completed, cancellationToken: CT);
        Assert.Empty(page);
        Assert.Equal(0, await store.GetJobCountByStatusAsync("Q", QueueJobStatus.Completed, CT));
    }

    [Fact]
    public async Task GetJobsByStatus_RemovesDanglingMembers_WhenIndexOutlivesThem()
    {
        var store = CreateStore(out var prefix);
        var setKey = StatusSetKey(prefix, "Q", QueueJobStatus.Completed);
        var baseTime = DateTimeOffset.UtcNow;

        // The oldest job keeps the index alive after the 200 short-lived ones behind it expire
        await store.SetJobStateAsync(CreateJobState("keeper", "Q", QueueJobStatus.Completed, baseTime), cancellationToken: CT);
        await Task.WhenAll(Enumerable.Range(1, 200).Select(i => store.SetJobStateAsync(
            CreateJobState($"job-{i}", "Q", QueueJobStatus.Completed, baseTime.AddMilliseconds(i)),
            expiry: TimeSpan.FromSeconds(1),
            cancellationToken: CT)));

        await Task.Delay(TimeSpan.FromSeconds(2), CT);

        Assert.Equal(201, await Db.SortedSetLengthAsync(setKey));

        var page = await store.GetJobsByStatusAsync("Q", QueueJobStatus.Completed, cancellationToken: CT);
        Assert.Single(page);
        Assert.Equal("keeper", page[0].JobId);

        Assert.Equal(1, await Db.SortedSetLengthAsync(setKey));
        Assert.Equal(1, await store.GetJobCountByStatusAsync("Q", QueueJobStatus.Completed, CT));
    }

    [Fact]
    public async Task GetJobsByStatus_SkipsExpiredMembers_AndStillFillsThePage()
    {
        var store = CreateStore(out var prefix);
        var baseTime = DateTimeOffset.UtcNow;

        // Newest 5 expire quickly; the 5 behind them stay alive
        for (int i = 1; i <= 5; i++)
            await store.SetJobStateAsync(CreateJobState($"live-{i}", "Q", QueueJobStatus.Completed, baseTime.AddSeconds(i)), cancellationToken: CT);
        for (int i = 1; i <= 5; i++)
            await store.SetJobStateAsync(CreateJobState($"gone-{i}", "Q", QueueJobStatus.Completed, baseTime.AddSeconds(10 + i)), expiry: TimeSpan.FromSeconds(1), cancellationToken: CT);

        await Task.Delay(TimeSpan.FromSeconds(2), CT);

        // A shorter-lived write must not have lowered the index TTL below the live members' 24h
        var setTtl = await Db.KeyTimeToLiveAsync(StatusSetKey(prefix, "Q", QueueJobStatus.Completed));
        Assert.NotNull(setTtl);
        Assert.InRange(setTtl.Value, TimeSpan.FromHours(23), TimeSpan.FromHours(24));

        var page = await store.GetJobsByStatusAsync("Q", QueueJobStatus.Completed, take: 3, cancellationToken: CT);
        Assert.Equal(3, page.Count);
        Assert.All(page, j => Assert.StartsWith("live-", j.JobId));
        Assert.Equal("live-5", page[0].JobId);

        Assert.Equal(5, await Db.SortedSetLengthAsync(StatusSetKey(prefix, "Q", QueueJobStatus.Completed)));
    }

    [Fact]
    public async Task Write_TrimsMembersOlderThanExpiryWindow()
    {
        var store = CreateStore(out var prefix, o => o.NonTerminalExpiry = TimeSpan.FromMilliseconds(500));
        var setKey = StatusSetKey(prefix, "Q", QueueJobStatus.Completed);
        var baseTime = DateTimeOffset.UtcNow;

        for (int i = 1; i <= 20; i++)
            await store.SetJobStateAsync(CreateJobState($"job-{i}", "Q", QueueJobStatus.Completed, baseTime), expiry: TimeSpan.FromMilliseconds(500), cancellationToken: CT);

        Assert.Equal(20, await Db.SortedSetLengthAsync(setKey));

        await Task.Delay(TimeSpan.FromSeconds(2), CT);

        // Any write to the set removes members created before now - (ttl + NonTerminalExpiry)
        await store.SetJobStateAsync(CreateJobState("fresh", "Q", QueueJobStatus.Completed), expiry: TimeSpan.FromMinutes(5), cancellationToken: CT);
        Assert.Equal(1, await Db.SortedSetLengthAsync(setKey));
        Assert.Equal(1, await store.GetJobCountByStatusAsync("Q", QueueJobStatus.Completed, CT));
    }

    // ── Cancellation ───────────────────────────────────────────────────────

    [Fact]
    public async Task RequestCancellation_SetsFlag_WithJobTtl()
    {
        var store = CreateStore(out var prefix);
        await store.SetJobStateAsync(CreateJobState(), expiry: TimeSpan.FromMinutes(5), cancellationToken: CT);

        var result = await store.RequestCancellationAsync("job-1", CT);
        Assert.True(result);

        var isCancelled = await store.IsCancellationRequestedAsync("job-1", CT);
        Assert.True(isCancelled);

        var jobTtl = await Db.KeyTimeToLiveAsync(JobKey(prefix, "job-1"));
        var cancelTtl = await Db.KeyTimeToLiveAsync(CancelKey(prefix, "job-1"));
        Assert.NotNull(jobTtl);
        Assert.NotNull(cancelTtl);
        Assert.InRange((jobTtl.Value - cancelTtl.Value).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(5));

        // Status updates keep the cancel key's TTL in step with the job
        await store.UpdateJobStatusAsync("job-1", QueueJobStatus.Processing, expiry: TimeSpan.FromMinutes(1), cancellationToken: CT);
        jobTtl = await Db.KeyTimeToLiveAsync(JobKey(prefix, "job-1"));
        cancelTtl = await Db.KeyTimeToLiveAsync(CancelKey(prefix, "job-1"));
        Assert.NotNull(jobTtl);
        Assert.NotNull(cancelTtl);
        Assert.InRange((jobTtl.Value - cancelTtl.Value).Duration(), TimeSpan.Zero, TimeSpan.FromSeconds(5));
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
        var store = CreateStore(out var prefix);
        await store.SetJobStateAsync(CreateJobState(status: QueueJobStatus.Completed), cancellationToken: CT);
        Assert.False(await store.RequestCancellationAsync("job-1", CT));
        Assert.False(await Db.KeyExistsAsync(CancelKey(prefix, "job-1")));

        var store2 = CreateStore();
        await store2.SetJobStateAsync(CreateJobState(status: QueueJobStatus.Failed), cancellationToken: CT);
        Assert.False(await store2.RequestCancellationAsync("job-1", CT));

        var store3 = CreateStore();
        await store3.SetJobStateAsync(CreateJobState(status: QueueJobStatus.Cancelled), cancellationToken: CT);
        Assert.False(await store3.RequestCancellationAsync("job-1", CT));

        // A job that reaches a terminal state through UpdateJobStatusAsync is no longer cancellable either
        var store4 = CreateStore();
        await store4.SetJobStateAsync(CreateJobState(status: QueueJobStatus.Processing), cancellationToken: CT);
        Assert.True(await store4.RequestCancellationAsync("job-1", CT));
        await store4.UpdateJobStatusAsync("job-1", QueueJobStatus.Completed, completedUtc: DateTimeOffset.UtcNow, cancellationToken: CT);
        Assert.False(await store4.RequestCancellationAsync("job-1", CT));
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

        // Removing a job that is already gone is a no-op
        await store.RemoveJobStateAsync("job-1", CT);
    }

    [Fact]
    public async Task RemoveJobState_RemovesFromStatusListing()
    {
        var store = CreateStore();
        await store.SetJobStateAsync(CreateJobState("job-1", "QueueA"), cancellationToken: CT);
        await store.SetJobStateAsync(CreateJobState("job-2", "QueueA"), cancellationToken: CT);

        await store.RemoveJobStateAsync("job-1", CT);

        var jobs = await store.GetJobsByStatusAsync("QueueA", QueueJobStatus.Queued, cancellationToken: CT);
        Assert.Single(jobs);
        Assert.Equal("job-2", jobs[0].JobId);
        Assert.Equal(1, await store.GetJobCountByStatusAsync("QueueA", QueueJobStatus.Queued, CT));
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
        var store = CreateStore(out var prefix);

        // Terminal states take the caller's expiry as is
        await store.SetJobStateAsync(CreateJobState("done", status: QueueJobStatus.Completed), expiry: TimeSpan.FromMinutes(5), cancellationToken: CT);
        var ttl = await Db.KeyTimeToLiveAsync(JobKey(prefix, "done"));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));

        var setTtl = await Db.KeyTimeToLiveAsync(StatusSetKey(prefix, "TestQueue", QueueJobStatus.Completed));
        Assert.NotNull(setTtl);
        Assert.InRange(setTtl.Value, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));

        // Live states are raised to NonTerminalExpiry so they do not vanish mid-flight
        await store.SetJobStateAsync(CreateJobState("live"), expiry: TimeSpan.FromMinutes(5), cancellationToken: CT);
        ttl = await Db.KeyTimeToLiveAsync(JobKey(prefix, "live"));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromDays(7) - TimeSpan.FromMinutes(1), TimeSpan.FromDays(7));

        // ...unless the caller asked for longer
        await store.SetJobStateAsync(CreateJobState("long-live"), expiry: TimeSpan.FromDays(10), cancellationToken: CT);
        ttl = await Db.KeyTimeToLiveAsync(JobKey(prefix, "long-live"));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromDays(10) - TimeSpan.FromMinutes(1), TimeSpan.FromDays(10));

        // Completing the job applies the terminal expiry again
        await store.UpdateJobStatusAsync("live", QueueJobStatus.Completed, expiry: TimeSpan.FromMinutes(5), cancellationToken: CT);
        ttl = await Db.KeyTimeToLiveAsync(JobKey(prefix, "live"));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));

        // No caller expiry falls back to DefaultExpiry; null disables TTL entirely
        var noExpiryStore = CreateStore(out var noExpiryPrefix, o => o.DefaultExpiry = null);
        await noExpiryStore.SetJobStateAsync(CreateJobState("forever", status: QueueJobStatus.Completed), cancellationToken: CT);
        Assert.Null(await Db.KeyTimeToLiveAsync(JobKey(noExpiryPrefix, "forever")));

        var defaultStore = CreateStore(out var defaultPrefix, o => o.DefaultExpiry = TimeSpan.FromMinutes(30));
        await defaultStore.SetJobStateAsync(CreateJobState("default", status: QueueJobStatus.Completed), cancellationToken: CT);
        ttl = await Db.KeyTimeToLiveAsync(JobKey(defaultPrefix, "default"));
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(30));
    }

    // ── Counters ───────────────────────────────────────────────────────────

    [Fact]
    public async Task IncrementCounter_CreatesAndIncrements()
    {
        var store = CreateStore();

        await store.IncrementCounterAsync("TestQueue", "processed", 1, CT);
        await store.IncrementCounterAsync("TestQueue", "processed", 1, CT);
        await store.IncrementCounterAsync("TestQueue", "failed", 1, CT);

        var stats = await store.GetCounterStatsAsync("TestQueue", TimeSpan.FromHours(1), CT);
        Assert.Equal(2, stats.Totals["processed"]);
        Assert.Equal(1, stats.Totals["failed"]);
    }

    [Fact]
    public async Task IncrementCounter_SupportsCustomIncrements()
    {
        var store = CreateStore();

        await store.IncrementCounterAsync("TestQueue", "processed", 5, CT);
        await store.IncrementCounterAsync("TestQueue", "processed", 10, CT);

        var stats = await store.GetCounterStatsAsync("TestQueue", TimeSpan.FromHours(1), CT);
        Assert.Equal(15, stats.Totals["processed"]);
    }

    [Fact]
    public async Task IncrementCounter_BucketExpires()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 1, 10, 30, 0, TimeSpan.Zero));
        var store = CreateStore(out var prefix, timeProvider: clock);

        await store.IncrementCounterAsync("TestQueue", "processed", 1, CT);

        var ttl = await Db.KeyTimeToLiveAsync($"{prefix}:counters:TestQueue:2026-03-01T10");
        Assert.NotNull(ttl);
        Assert.InRange(ttl.Value, TimeSpan.FromHours(47), TimeSpan.FromHours(48));
    }

    [Fact]
    public async Task GetCounterStats_EmptyQueue_ReturnsEmptyTotals()
    {
        var store = CreateStore();
        var stats = await store.GetCounterStatsAsync("NonExistent", TimeSpan.FromHours(1), CT);
        Assert.Empty(stats.Totals);
        Assert.NotEmpty(stats.Buckets); // Should still have hourly bucket entries (with empty counters)
    }

    [Fact]
    public async Task Counters_IsolatedPerQueue()
    {
        var store = CreateStore();

        await store.IncrementCounterAsync("Queue1", "processed", 3, CT);
        await store.IncrementCounterAsync("Queue2", "processed", 7, CT);

        var stats1 = await store.GetCounterStatsAsync("Queue1", TimeSpan.FromHours(1), CT);
        var stats2 = await store.GetCounterStatsAsync("Queue2", TimeSpan.FromHours(1), CT);

        Assert.Equal(3, stats1.Totals["processed"]);
        Assert.Equal(7, stats2.Totals["processed"]);
    }

    [Fact]
    public async Task GetCounterStats_ReturnsBucketsForWindow()
    {
        var store = CreateStore();

        await store.IncrementCounterAsync("TestQueue", "processed", 5, CT);

        var stats = await store.GetCounterStatsAsync("TestQueue", TimeSpan.FromHours(24), CT);

        // Should have 25 buckets (24 hours ago through current hour)
        Assert.Equal(25, stats.Buckets.Count);

        // At least one bucket should have the counter
        Assert.Contains(stats.Buckets, b => b.Counters.GetValueOrDefault("processed") > 0);

        // Buckets should be ordered oldest to newest
        for (int i = 1; i < stats.Buckets.Count; i++)
            Assert.True(stats.Buckets[i].Hour > stats.Buckets[i - 1].Hour);
    }

    [Fact]
    public async Task GetCounterStats_BucketsAcrossHourBoundary()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 1, 10, 59, 0, TimeSpan.Zero));
        var store = CreateStore(out _, timeProvider: clock);

        await store.IncrementCounterAsync("Q", "processed", 3, CT);
        await store.IncrementCounterAsync("Q", "failed", 1, CT);

        clock.Advance(TimeSpan.FromMinutes(2)); // 11:01
        await store.IncrementCounterAsync("Q", "processed", 4, CT);

        var stats = await store.GetCounterStatsAsync("Q", TimeSpan.FromHours(1), CT);

        Assert.Equal(2, stats.Buckets.Count);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero), stats.Buckets[0].Hour);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 11, 0, 0, TimeSpan.Zero), stats.Buckets[1].Hour);
        Assert.Equal(3, stats.Buckets[0].Counters["processed"]);
        Assert.Equal(1, stats.Buckets[0].Counters["failed"]);
        Assert.Equal(4, stats.Buckets[1].Counters["processed"]);
        Assert.False(stats.Buckets[1].Counters.ContainsKey("failed"));
        Assert.Equal(7, stats.Totals["processed"]);
        Assert.Equal(1, stats.Totals["failed"]);

        // A window that ends before the older bucket excludes it
        clock.Advance(TimeSpan.FromHours(1)); // 12:01
        stats = await store.GetCounterStatsAsync("Q", TimeSpan.FromHours(1), CT);
        Assert.Equal(2, stats.Buckets.Count);
        Assert.Equal(4, stats.Totals["processed"]);
        Assert.False(stats.Totals.ContainsKey("failed"));
    }
}
