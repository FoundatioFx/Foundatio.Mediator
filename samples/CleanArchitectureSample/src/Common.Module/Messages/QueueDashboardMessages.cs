namespace Common.Module.Messages;

// ── Reads (anonymous) ──

public record GetQueues;

public record GetQueue(string QueueName);

public record GetJobDashboard(string QueueName, string Status = "active", int Skip = 0, int Take = 25);

public record GetQueueJobDetail(string JobId);

public record GetDeadLetters(string QueueName, int Take = 20);

public record GetHostInfo;

// ── Operations (Admin role) ──

public record CancelJob(string JobId);

public record ReplayQueueDeadLetters(string QueueName, int Max = 100, string? MessageId = null);

public record PurgeQueueDeadLetters(string QueueName, int Max = 1000);

public record EnqueueDemoJob(int Count = 1, int Steps = 20, int StepDelayMs = 1500, int FailTimes = 0, bool CriticalFailure = false);

public record EnqueueImportJob(int Count = 1, int Rows = 200, int RowDelayMs = 50);

public record EnqueueFlakyWebhook(string Url = "https://hooks.example.com/orders", int FailTimes = 3);

public record EnqueueBankFiles(string Bank = "first-national", int Count = 2);

// ── DTOs ──

public record QueueSummary
{
    public required string QueueName { get; init; }
    public required string MessageType { get; init; }
    public required IReadOnlyList<string> Handlers { get; init; }
    public string? Group { get; init; }
    public string? Description { get; init; }
    public int Concurrency { get; init; }
    public int PrefetchCount { get; init; }
    public bool AutoComplete { get; init; }
    public bool AutoRenewTimeout { get; init; }
    public string? RetryDelays { get; init; }
    public int MaxAttempts { get; init; }
    public required string RetryPolicy { get; init; }
    public int VisibilityTimeoutSeconds { get; init; }
    public bool TrackProgress { get; init; }

    /// <summary>Whether the node that answered this request runs a worker for the queue.</summary>
    public bool WorkerRunsHere { get; init; }
    public bool? IsRunning { get; init; }
    public long MessagesProcessed { get; init; }
    public long MessagesFailed { get; init; }
    public long MessagesDeadLettered { get; init; }
    public bool StatisticsAvailable { get; init; }
    public long ActiveCount { get; init; }
    public long DelayedCount { get; init; }
    public long InFlightCount { get; init; }
    public long DeadLetterCount { get; init; }
    public CounterStatsView? CounterStats { get; init; }
}

public record JobSummary
{
    public required string JobId { get; init; }
    public required string QueueName { get; init; }
    public required string MessageType { get; init; }
    public required string Status { get; init; }
    public int Progress { get; init; }
    public string? ProgressMessage { get; init; }
    public int Attempt { get; init; }
    public string? WorkerId { get; init; }
    public bool CancellationRequested { get; init; }
    public DateTimeOffset LastUpdatedUtc { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset? StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public DateTimeOffset? LastHeartbeatUtc { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Captured at enqueue time by <c>DistributedQueueOptions.JobMetadataProvider</c>: tenant and user here.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public record JobDashboardView
{
    public required IReadOnlyDictionary<string, long> Counts { get; init; }
    public required IReadOnlyList<JobSummary> Jobs { get; init; }
    public long Total { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
}

public record CounterStatsView
{
    public required IReadOnlyDictionary<string, long> Totals { get; init; }
    public required IReadOnlyList<CounterBucketView> Buckets { get; init; }
}

public record CounterBucketView
{
    public DateTimeOffset Hour { get; init; }
    public required IReadOnlyDictionary<string, long> Counters { get; init; }
}

/// <summary>Which process answered, and which workers it runs.</summary>
public record HostInfoView(string HostId, string Workers);

/// <summary>What an enqueue control put on the queue. Job ids are present only for tracked queues.</summary>
public record EnqueueReceipt(string QueueName, int Count, IReadOnlyList<string> JobIds);
