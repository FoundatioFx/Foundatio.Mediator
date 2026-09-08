using Foundatio.Messaging;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

// ── Messages ─────────────────────────────────────────────────────────

/// <summary>Lists every queue known to this process with depth, worker, and counter information.</summary>
public record GetQueueOverview;

/// <summary>Describes one queue.</summary>
public record GetQueueDetail(string QueueName);

/// <summary>Lists tracked jobs on a queue by status, newest first.</summary>
public record ListQueueJobs(string QueueName, MessageExecutionStatus Status, int Skip = 0, int Take = 50);

/// <summary>Gets one tracked job.</summary>
public record GetQueueJob(string JobId);

/// <summary>Requests cancellation of a tracked job. The worker observes it on its next progress report or poll.</summary>
public record CancelQueueJob(string JobId);

/// <summary>Peeks at dead-lettered messages without removing them.</summary>
public record ListDeadLetters(string QueueName, int Take = 20);

/// <summary>Sends dead-lettered messages back to their original queue. <paramref name="MessageId"/> limits the replay to one message.</summary>
public record ReplayDeadLetters(string QueueName, int Max = 100, string? MessageId = null);

/// <summary>Permanently deletes dead-lettered messages. <paramref name="MessageId"/> limits deletion to one message.</summary>
/// <param name="QueueName">The original queue name.</param>
/// <param name="Max">Maximum number of available messages to inspect, clamped to 1–100,000.</param>
/// <param name="MessageId">The dead-letter message id to delete, or <c>null</c> to delete all inspected messages.</param>
public record PurgeDeadLetters(string QueueName, int Max = 1000, string? MessageId = null);

// ── Views ────────────────────────────────────────────────────────────

/// <summary>Queue depth, configuration, and processing counters.</summary>
public sealed record QueueOverview
{
    public required string QueueName { get; init; }
    /// <summary>The configured display label, or <c>null</c> to display <see cref="QueueName"/>.</summary>
    public string? DisplayName { get; init; }
    public required string MessageType { get; init; }
    public required IReadOnlyList<string> Handlers { get; init; }
    public string? Group { get; init; }
    public string? Description { get; init; }
    public int Concurrency { get; init; }
    public int MaxAttempts { get; init; }
    public required string RetryPolicy { get; init; }
    public TimeSpan VisibilityTimeout { get; init; }
    public bool TrackProgress { get; init; }

    /// <summary>Whether a worker for this queue runs in the process answering the query.</summary>
    public bool WorkerRunsHere { get; init; }

    /// <summary><c>null</c> when no worker runs here.</summary>
    public bool? IsRunning { get; init; }

    /// <summary>Whether transport statistics were available; counts must not be interpreted as zero when false.</summary>
    public bool StatisticsAvailable { get; init; }

    public long ActiveCount { get; init; }
    /// <summary>Messages scheduled for later delivery or waiting for retry.</summary>
    public long DelayedCount { get; init; }
    public long InFlightCount { get; init; }
    public long DeadLetterCount { get; init; }

    /// <summary>Processing counters over the last 24 hours across every worker, when a job state store records them; otherwise this process only.</summary>
    public long Processed { get; init; }
    public long Failed { get; init; }
    public long DeadLettered { get; init; }
    public MessageExecutionCounters? Counters { get; init; }
}

/// <summary>A dead-lettered message.</summary>
public sealed record DeadLetterView
{
    public required string MessageId { get; init; }
    public required string QueueName { get; init; }
    public string? OriginalQueueName { get; init; }
    public string? MessageType { get; init; }
    public string? Reason { get; init; }
    public DateTimeOffset? DeadLetteredAt { get; init; }
    public int? Attempts { get; init; }
    public string? JobId { get; init; }
    public string? CorrelationId { get; init; }
    /// <summary>The prior tracked execution when this message was replayed.</summary>
    public string? OriginalJobId { get; init; }
    /// <summary>When the operator replayed this message.</summary>
    public DateTimeOffset? ReplayedAt { get; init; }

    /// <summary>The message body as text, truncated to 4,096 characters.</summary>
    public required string Body { get; init; }
    /// <summary>Whether the body exceeds the preview limit.</summary>
    public bool BodyTruncated { get; init; }
    /// <summary>Message headers, including correlation, tenant context, and replay lineage.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
}

public sealed record DeadLetterReplayResult(string QueueName, int Replayed, int Skipped)
{
    /// <summary>Accepted replays, including fresh job identities for tracked messages.</summary>
    public IReadOnlyList<QueueReceipt> Receipts { get; init; } = [];
}
public sealed record DeadLetterPurgeResult(string QueueName, int Purged);
public sealed record QueueJobCancellation(string JobId, bool CancellationRequested);

// ── Handler ──────────────────────────────────────────────────────────

/// <summary>
/// Mediator handlers for operating queues: listing depth and workers, inspecting and cancelling tracked
/// jobs, and peeking, replaying, or purging dead letters. They have no HTTP surface of their own; expose
/// them from an endpoint handler in the host with whatever authorization the host requires.
/// </summary>
public class QueueAdministrationHandler(
    QueueTopology topology,
    IQueueWorkerRegistry workers,
    IMessageTransport transport,
    ILogger<QueueAdministrationHandler> logger,
    IMessageExecutionStore? stateStore = null,
    TimeProvider? timeProvider = null)
{
    private const int MaxBodyPreview = 4096;
    private readonly MessageAdministration _administration = new(transport, timeProvider);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    // Long enough for SQS to consult every server, short enough that administration calls stay snappy.

    public async Task<Result<IReadOnlyList<QueueOverview>>> HandleAsync(GetQueueOverview query, CancellationToken ct)
    {
        var queueNames = topology.Queues.Select(q => q.QueueName).ToList();
        var statsByQueue = await SafeStatsAsync(queueNames, ct).ConfigureAwait(false);

        var results = new List<QueueOverview>(queueNames.Count);
        foreach (var registration in topology.Queues)
            results.Add(await ToOverviewAsync(registration, statsByQueue.GetValueOrDefault(registration.QueueName), ct).ConfigureAwait(false));

        return results;
    }

    public async Task<Result<QueueOverview>> HandleAsync(GetQueueDetail query, CancellationToken ct)
    {
        var registration = topology.GetByQueueName(query.QueueName);
        if (registration is null)
            return Result.NotFound($"Queue '{query.QueueName}' is not registered.");

        var stats = await SafeStatsAsync([registration.QueueName], ct).ConfigureAwait(false);
        return await ToOverviewAsync(registration, stats.GetValueOrDefault(registration.QueueName), ct).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<MessageExecutionState>>> HandleAsync(ListQueueJobs query, CancellationToken ct)
    {
        if (stateStore is null)
            return Result.Invalid("Job tracking is not configured; register an IMessageExecutionStore.");

        if (topology.GetByQueueName(query.QueueName) is null)
            return Result.NotFound($"Queue '{query.QueueName}' is not registered.");

        var jobs = await stateStore.GetJobsByStatusAsync(query.QueueName, query.Status, Math.Max(0, query.Skip), Math.Clamp(query.Take, 1, 500), ct).ConfigureAwait(false);
        return Result<IReadOnlyList<MessageExecutionState>>.Ok(jobs);
    }

    public async Task<Result<MessageExecutionState>> HandleAsync(GetQueueJob query, CancellationToken ct)
    {
        if (stateStore is null)
            return Result.Invalid("Job tracking is not configured; register an IMessageExecutionStore.");

        var state = await stateStore.GetJobStateAsync(query.JobId, ct).ConfigureAwait(false);
        return state is null ? Result.NotFound($"Job '{query.JobId}' was not found.") : state;
    }

    public async Task<Result<QueueJobCancellation>> HandleAsync(CancelQueueJob command, CancellationToken ct)
    {
        if (stateStore is null)
            return Result.Invalid("Job tracking is not configured; register an IMessageExecutionStore.");

        var requested = await stateStore.RequestCancellationAsync(command.JobId, ct).ConfigureAwait(false);
        if (!requested)
            return Result.NotFound($"Job '{command.JobId}' was not found or has already finished.");

        logger.LogInformation("Cancellation requested for job {JobId}", command.JobId);
        return new QueueJobCancellation(command.JobId, true);
    }

    public async Task<Result<IReadOnlyList<DeadLetterView>>> HandleAsync(ListDeadLetters query, CancellationToken ct)
    {
        if (topology.GetByQueueName(query.QueueName) is null) return Result.NotFound($"Queue '{query.QueueName}' is not registered.");
        var entries = await _administration.PeekDeadLettersAsync(DestinationAddress.ForQueue(query.QueueName), Math.Clamp(query.Take, 1, 100), ct).ConfigureAwait(false);
        return Result<IReadOnlyList<DeadLetterView>>.Ok(entries.Select(entry => ToView(entry, query.QueueName)).ToArray());
    }

    public async Task<Result<DeadLetterReplayResult>> HandleAsync(ReplayDeadLetters command, CancellationToken ct)
    {
        var registration = topology.GetByQueueName(command.QueueName);
        if (registration is null) return Result.NotFound($"Queue '{command.QueueName}' is not registered.");
        var source = DestinationAddress.ForQueue(command.QueueName);
        var receipts = new List<QueueReceipt>();
        var ids = command.MessageId is { } id ? new[] { id }
            : (await _administration.PeekDeadLettersAsync(source, Math.Clamp(command.Max, 1, 1000), ct).ConfigureAwait(false)).Select(entry => entry.Id).ToArray();
        foreach (var messageId in ids)
        {
            string? executionId = registration.Settings.TrackProgress && stateStore is not null ? Guid.NewGuid().ToString("N") : null;
            try
            {
                if (await _administration.ReplayDeadLetterAsync(source, messageId, async (entry, token) =>
                {
                    var headers = entry.Headers.ToBuilder().Set(ExecutionHeaders.ReplayedAt, _timeProvider.GetUtcNow().ToString("O"));
                    headers.Remove(KnownHeaders.Attempts);
                    if (entry.Headers.TryGetValue(ExecutionHeaders.ExecutionId, out var original)) headers.Set(ExecutionHeaders.OriginalExecutionId, original);
                    if (executionId is not null)
                    {
                        headers.Set(ExecutionHeaders.ExecutionId, executionId);
                        await stateStore!.SetJobStateAsync(new MessageExecutionState
                        {
                            JobId = executionId,
                            QueueName = source.Name,
                            MessageType = entry.Headers.GetValueOrDefault(KnownHeaders.MessageType) ?? registration.MessageType.Name,
                            Status = MessageExecutionStatus.Queued,
                            CreatedUtc = _timeProvider.GetUtcNow(),
                            LastUpdatedUtc = _timeProvider.GetUtcNow()
                        }, cancellationToken: token).ConfigureAwait(false);
                    }
                    return new TransportMessage { Body = entry.Body, ContentType = entry.ContentType, Headers = headers.Build(), MessageId = Guid.NewGuid().ToString("N") };
                }, ct).ConfigureAwait(false)) receipts.Add(new QueueReceipt(source.Name, executionId));
            }
            catch (Exception exception)
            {
                if (executionId is not null && stateStore is not null)
                {
                    try
                    {
                        await QueueOperation.RunAsync(token => stateStore.UpdateJobStatusAsync(executionId, MessageExecutionStatus.EnqueueUnknown,
                            errorMessage: exception.Message, cancellationToken: token), TimeSpan.FromSeconds(5), _timeProvider).ConfigureAwait(false);
                    }
                    catch (Exception stateError) { logger.LogWarning(stateError, "Unable to record the uncertain replay outcome for {ExecutionId}", executionId); }
                }
                throw;
            }
        }
        return new DeadLetterReplayResult(source.Name, receipts.Count, ids.Length - receipts.Count) { Receipts = receipts };
    }

    public async Task<Result<DeadLetterPurgeResult>> HandleAsync(PurgeDeadLetters command, CancellationToken ct)
    {
        if (topology.GetByQueueName(command.QueueName) is null) return Result.NotFound($"Queue '{command.QueueName}' is not registered.");
        var source = DestinationAddress.ForQueue(command.QueueName);
        var ids = command.MessageId is { } id ? new[] { id }
            : (await _administration.PeekDeadLettersAsync(source, Math.Clamp(command.Max, 1, 1000), ct).ConfigureAwait(false)).Select(entry => entry.Id).ToArray();
        int count = 0;
        foreach (var messageId in ids)
            if (await _administration.DeleteDeadLetterAsync(source, messageId, ct).ConfigureAwait(false)) count++;
        return new DeadLetterPurgeResult(source.Name, count);
    }

    private async Task<Dictionary<string, QueueStats>> SafeStatsAsync(IReadOnlyList<string> queueNames, CancellationToken ct)
    {
        try
        {
            var results = new Dictionary<string, QueueStats>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in queueNames)
            {
                var stats = await _administration.GetStatsAsync(DestinationAddress.ForQueue(name), ct).ConfigureAwait(false);
                results[name] = new QueueStats
                {
                    QueueName = name,
                    ActiveCount = stats.Queued,
                    DelayedCount = stats.Delayed ?? 0,
                    InFlightCount = stats.Working,
                    DeadLetterCount = stats.Deadletter
                };
            }
            return results;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to read queue statistics");
            return new Dictionary<string, QueueStats>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<QueueOverview> ToOverviewAsync(QueueRegistration registration, QueueStats? stats, CancellationToken ct)
    {
        var worker = workers.GetWorker(registration.QueueName);
        MessageExecutionCounters? counters = null;
        if (stateStore is not null)
        {
            try { counters = await stateStore.GetCounterStatsAsync(registration.QueueName, TimeSpan.FromHours(24), ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Failed to read counters for {QueueName}", registration.QueueName); }
        }

        return new QueueOverview
        {
            QueueName = registration.QueueName,
            DisplayName = registration.DisplayName,
            MessageType = registration.MessageType.FullName ?? registration.MessageType.Name,
            Handlers = registration.Handlers.Select(h => h.SourceHandlerName ?? h.DescriptorId).ToList(),
            Group = registration.Settings.Group,
            Description = registration.Settings.Description,
            Concurrency = worker?.Concurrency ?? registration.Settings.Concurrency,
            MaxAttempts = registration.Settings.MaxAttempts,
            RetryPolicy = worker?.RetryPolicy.ToString() ?? registration.Settings.RetryPolicy.ToString(),
            VisibilityTimeout = worker?.VisibilityTimeout ?? TimeSpan.FromSeconds(registration.Settings.TimeoutSeconds),
            TrackProgress = registration.Settings.TrackProgress,
            WorkerRunsHere = registration.WorkerRunsHere,
            IsRunning = worker is { Stats.WorkerRegistered: true } ? worker.Stats.IsRunning : null,
            StatisticsAvailable = stats is not null,
            ActiveCount = stats?.ActiveCount ?? 0,
            DelayedCount = stats?.DelayedCount ?? 0,
            InFlightCount = stats?.InFlightCount ?? 0,
            DeadLetterCount = stats?.DeadLetterCount ?? 0,
            Processed = counters?.Totals.GetValueOrDefault("processed") ?? worker?.Stats.MessagesProcessed ?? 0,
            Failed = counters?.Totals.GetValueOrDefault("failed") ?? worker?.Stats.MessagesFailed ?? 0,
            DeadLettered = counters?.Totals.GetValueOrDefault("dead_lettered") ?? worker?.Stats.MessagesDeadLettered ?? 0,
            Counters = counters
        };
    }

    private static DeadLetterView ToView(TransportEntry message, string queueName)
    {
        var body = Encoding.UTF8.GetString(message.Body.Span);
        bool truncated = body.Length > MaxBodyPreview;
        if (truncated)
            body = body[..MaxBodyPreview];

        return new DeadLetterView
        {
            MessageId = message.Id,
            QueueName = queueName,
            OriginalQueueName = message.Headers.GetValueOrDefault(KnownHeaders.DeadLetterOriginalDestination),
            MessageType = message.Headers.GetValueOrDefault(KnownHeaders.MessageType),
            Reason = message.Headers.GetValueOrDefault(KnownHeaders.DeadLetterReason),
            DeadLetteredAt = DateTimeOffset.TryParse(message.Headers.GetValueOrDefault(KnownHeaders.DeadLetterFailedAt), out var at) ? at : null,
            Attempts = int.TryParse(message.Headers.GetValueOrDefault(KnownHeaders.DeadLetterAttempts), out var attempts) ? attempts : null,
            JobId = message.Headers.GetValueOrDefault(ExecutionHeaders.ExecutionId),
            CorrelationId = message.Headers.GetValueOrDefault(KnownHeaders.CorrelationId),
            OriginalJobId = message.Headers.GetValueOrDefault(ExecutionHeaders.OriginalExecutionId),
            ReplayedAt = DateTimeOffset.TryParse(message.Headers.GetValueOrDefault(ExecutionHeaders.ReplayedAt), out var replayedAt) ? replayedAt : null,
            Headers = new Dictionary<string, string>(message.Headers),
            BodyTruncated = truncated,
            Body = body
        };
    }
}
