namespace Common.Module.Messages;

// ── Queue Dashboard Queries ──

public record GetQueues;

public record GetQueue(string QueueName);

public record GetJobDashboard(string QueueName, int? RecentTerminalCount = 20);

public record GetQueueJobDetail(string JobId);

public record CancelJob(string JobId);

public record EnqueueDemoJob(int Count = 1, int Steps = 20, int StepDelayMs = 1500);

// ── DTOs ──

public record QueueSummary
{
    public required string QueueName { get; init; }
    public required string MessageType { get; init; }
    public int Concurrency { get; init; }
    public int MaxRetries { get; init; }
    public required string RetryPolicy { get; init; }
    public bool TrackProgress { get; init; }
    public bool IsRunning { get; init; }
    public long MessagesProcessed { get; init; }
    public long MessagesFailed { get; init; }
    public long MessagesDeadLettered { get; init; }
    public long ActiveCount { get; init; }
    public long DeadLetterCount { get; init; }
    public long InFlightCount { get; init; }
}

public record JobSummary
{
    public required string JobId { get; init; }
    public required string QueueName { get; init; }
    public required string MessageType { get; init; }
    public required string Status { get; init; }
    public int Progress { get; init; }
    public string? ProgressMessage { get; init; }
    public DateTimeOffset CreatedUtc { get; init; }
    public DateTimeOffset? StartedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public string? ErrorMessage { get; init; }
}

public record JobCancellationResult(string JobId, bool CancellationRequested);

public record JobDashboardView
{
    public long QueuedCount { get; init; }
    public required List<JobSummary> ActiveJobs { get; init; }
    public required List<JobSummary> RecentJobs { get; init; }
}

public record DemoJobEnqueued(string JobId);

// ── Demo message that gets queued with progress tracking ──

public record DemoExportJob(int Steps = 20, int StepDelayMs = 1500);
