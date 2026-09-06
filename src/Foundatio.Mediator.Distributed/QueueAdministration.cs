using System.Text;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator.Distributed;

// ── Messages ─────────────────────────────────────────────────────────

/// <summary>Lists every queue known to this process with depth, worker, and counter information.</summary>
public record GetQueueOverview;

/// <summary>Describes one queue.</summary>
public record GetQueueDetail(string QueueName);

/// <summary>Lists tracked jobs on a queue by status, newest first.</summary>
public record ListQueueJobs(string QueueName, QueueJobStatus Status, int Skip = 0, int Take = 50);

/// <summary>Gets one tracked job.</summary>
public record GetQueueJob(string JobId);

/// <summary>Requests cancellation of a tracked job. The worker observes it on its next progress report or poll.</summary>
public record CancelQueueJob(string JobId);

/// <summary>Peeks at dead-lettered messages without removing them.</summary>
public record ListDeadLetters(string QueueName, int Take = 20);

/// <summary>Sends dead-lettered messages back to their original queue. <paramref name="MessageId"/> limits the replay to one message.</summary>
public record ReplayDeadLetters(string QueueName, int Max = 100, string? MessageId = null);

/// <summary>Permanently deletes dead-lettered messages.</summary>
public record PurgeDeadLetters(string QueueName, int Max = 1000);

// ── Views ────────────────────────────────────────────────────────────

/// <summary>Queue depth, configuration, and processing counters.</summary>
public sealed record QueueOverview
{
    public required string QueueName { get; init; }
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
    public QueueCounterStats? Counters { get; init; }
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
    IQueueClient client,
    ILogger<QueueAdministrationHandler> logger,
    IQueueJobStateStore? stateStore = null,
    TimeProvider? timeProvider = null)
{
    private const int MaxBodyPreview = 4096;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    // Long enough for SQS to consult every server, short enough that administration calls stay snappy.
    private static readonly TimeSpan s_adminPollWait = TimeSpan.FromSeconds(1);

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

    public async Task<Result<IReadOnlyList<QueueJobState>>> HandleAsync(ListQueueJobs query, CancellationToken ct)
    {
        if (stateStore is null)
            return Result.Invalid("Job tracking is not configured; register an IQueueJobStateStore.");

        if (topology.GetByQueueName(query.QueueName) is null)
            return Result.NotFound($"Queue '{query.QueueName}' is not registered.");

        var jobs = await stateStore.GetJobsByStatusAsync(query.QueueName, query.Status, Math.Max(0, query.Skip), Math.Clamp(query.Take, 1, 500), ct).ConfigureAwait(false);
        return Result<IReadOnlyList<QueueJobState>>.Ok(jobs);
    }

    public async Task<Result<QueueJobState>> HandleAsync(GetQueueJob query, CancellationToken ct)
    {
        if (stateStore is null)
            return Result.Invalid("Job tracking is not configured; register an IQueueJobStateStore.");

        var state = await stateStore.GetJobStateAsync(query.JobId, ct).ConfigureAwait(false);
        return state is null ? Result.NotFound($"Job '{query.JobId}' was not found.") : state;
    }

    public async Task<Result<QueueJobCancellation>> HandleAsync(CancelQueueJob command, CancellationToken ct)
    {
        if (stateStore is null)
            return Result.Invalid("Job tracking is not configured; register an IQueueJobStateStore.");

        var requested = await stateStore.RequestCancellationAsync(command.JobId, ct).ConfigureAwait(false);
        if (!requested)
            return Result.NotFound($"Job '{command.JobId}' was not found or has already finished.");

        logger.LogInformation("Cancellation requested for job {JobId}", command.JobId);
        return new QueueJobCancellation(command.JobId, true);
    }

    public async Task<Result<IReadOnlyList<DeadLetterView>>> HandleAsync(ListDeadLetters query, CancellationToken ct)
    {
        if (topology.GetByQueueName(query.QueueName) is null)
            return Result.NotFound($"Queue '{query.QueueName}' is not registered.");

        var take = Math.Clamp(query.Take, 1, 100);
        var views = new List<DeadLetterView>(take);
        var received = new List<QueueMessage>(take);

        // Receiving locks the messages; abandoning with no delay puts them straight back, so this is a peek.
        try
        {
            while (received.Count < take)
            {
                var batch = await ReceiveDeadLettersWithTimeoutAsync(query.QueueName, take - received.Count, ct).ConfigureAwait(false);
                if (batch.Count == 0)
                    break;
                received.AddRange(batch);
            }

            foreach (var message in received)
                views.Add(ToView(message));
        }
        finally
        {
            foreach (var message in received)
            {
                await ReleaseDeadLetterAsync(message).ConfigureAwait(false);
            }
        }

        return views;
    }

    public async Task<Result<DeadLetterReplayResult>> HandleAsync(ReplayDeadLetters command, CancellationToken ct)
    {
        if (topology.GetByQueueName(command.QueueName) is null)
            return Result.NotFound($"Queue '{command.QueueName}' is not registered.");

        int replayed = 0, skipped = 0;
        var receipts = new List<QueueReceipt>();
        var max = Math.Clamp(command.Max, 1, 10_000);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var startedAt = _timeProvider.GetUtcNow();

        while (replayed + skipped < max)
        {
            var batch = await ReceiveDeadLettersWithTimeoutAsync(command.QueueName, Math.Min(10, max - replayed - skipped), ct).ConfigureAwait(false);
            if (batch.Count == 0)
                break;

            bool progressed = false;
            var pending = new HashSet<QueueMessage>(batch);
            try
            {
                foreach (var message in batch)
                {
                    // A replayed message that fails again lands back here with a newer timestamp; leave those
                    // for the next operator decision instead of looping on them.
                    bool deadLetteredDuringReplay = DateTimeOffset.TryParse(message.Headers.GetValueOrDefault(MessageHeaders.DeadLetteredAt), out var deadLetteredAt)
                        && deadLetteredAt >= startedAt;

                    if (!seen.Add(message.Id) || deadLetteredDuringReplay)
                    {
                        await ReleaseDeadLetterAsync(message).ConfigureAwait(false);
                        pending.Remove(message);
                        continue;
                    }

                    progressed = true;
                    if (command.MessageId is not null && !string.Equals(command.MessageId, message.Id, StringComparison.Ordinal))
                    {
                        skipped++;
                        await ReleaseDeadLetterAsync(message).ConfigureAwait(false);
                        pending.Remove(message);
                        continue;
                    }

                    string? newJobId = null;
                    if (message.Headers.TryGetValue(MessageHeaders.JobId, out var originalJobId))
                    {
                        if (stateStore is null)
                            throw new InvalidOperationException("Replaying tracked work requires an IQueueJobStateStore.");
                        var original = await stateStore.GetJobStateAsync(originalJobId, ct).ConfigureAwait(false);
                        var now = _timeProvider.GetUtcNow();
                        newJobId = Guid.NewGuid().ToString("N");
                        await stateStore.SetJobStateAsync(new QueueJobState
                        {
                            JobId = newJobId, QueueName = command.QueueName,
                            MessageType = original?.MessageType ?? message.Headers.GetValueOrDefault(MessageHeaders.MessageType) ?? "",
                            CreatedUtc = now, LastUpdatedUtc = now, Metadata = original?.Metadata
                        }, cancellationToken: ct).ConfigureAwait(false);
                    }
                    try
                    {
                        await client.ReplayAsync(message, ct, newJobId).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        if (newJobId is not null)
                        {
                            try
                            {
                                await QueueOperation.RunAsync(token => stateStore!.UpdateJobStatusAsync(newJobId, QueueJobStatus.EnqueueUnknown,
                                    errorMessage: exception.Message, cancellationToken: token), TimeSpan.FromSeconds(5), _timeProvider).ConfigureAwait(false);
                            }
                            catch { /* Preserve the original replay failure. */ }
                        }
                        throw;
                    }
                    pending.Remove(message);
                    receipts.Add(new QueueReceipt(command.QueueName, newJobId));
                    replayed++;
                    logger.LogInformation("Replayed dead letter {MessageId} to {QueueName}", message.Id, command.QueueName);

                    if (command.MessageId is not null)
                        return new DeadLetterReplayResult(command.QueueName, replayed, skipped) { Receipts = receipts };
                }
            }
            finally
            {
                // A targeted replay or a failed request must release the rest of the received batch.
                foreach (var message in pending)
                    await ReleaseDeadLetterAsync(message).ConfigureAwait(false);
            }

            if (!progressed)
                break;
        }

        return new DeadLetterReplayResult(command.QueueName, replayed, skipped) { Receipts = receipts };
    }

    public async Task<Result<DeadLetterPurgeResult>> HandleAsync(PurgeDeadLetters command, CancellationToken ct)
    {
        if (topology.GetByQueueName(command.QueueName) is null)
            return Result.NotFound($"Queue '{command.QueueName}' is not registered.");

        int purged = 0;
        var max = Math.Clamp(command.Max, 1, 100_000);
        while (purged < max)
        {
            var batch = await ReceiveDeadLettersWithTimeoutAsync(command.QueueName, Math.Min(10, max - purged), ct).ConfigureAwait(false);
            if (batch.Count == 0)
                break;

            var pending = new HashSet<QueueMessage>(batch);
            try
            {
                foreach (var message in batch)
                {
                    await client.CompleteAsync(message, ct).ConfigureAwait(false);
                    pending.Remove(message);
                    purged++;
                }
            }
            finally
            {
                foreach (var message in pending)
                    await ReleaseDeadLetterAsync(message).ConfigureAwait(false);
            }
        }

        logger.LogWarning("Purged {Count} dead letter(s) from {QueueName}", purged, command.QueueName);
        return new DeadLetterPurgeResult(command.QueueName, purged);
    }

    private async Task ReleaseDeadLetterAsync(QueueMessage message)
    {
        try
        {
            await QueueOperation.RunAsync(token => client.AbandonAsync(message, TimeSpan.Zero, token),
                TimeSpan.FromSeconds(5), _timeProvider).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to release inspected dead letter {MessageId}", message.Id);
        }
    }

    private Task<IReadOnlyList<QueueMessage>> ReceiveDeadLettersWithTimeoutAsync(string queueName, int maxCount, CancellationToken ct)
        => client.ReceiveDeadLettersAsync(queueName, maxCount, s_adminPollWait, ct);

    private async Task<Dictionary<string, QueueStats>> SafeStatsAsync(IReadOnlyList<string> queueNames, CancellationToken ct)
    {
        try
        {
            var stats = await client.GetQueueStatsAsync(queueNames, ct).ConfigureAwait(false);
            return stats.ToDictionary(s => s.QueueName, StringComparer.OrdinalIgnoreCase);
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
        QueueCounterStats? counters = null;
        if (stateStore is not null)
        {
            try { counters = await stateStore.GetCounterStatsAsync(registration.QueueName, TimeSpan.FromHours(24), ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning(ex, "Failed to read counters for {QueueName}", registration.QueueName); }
        }

        return new QueueOverview
        {
            QueueName = registration.QueueName,
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

    private static DeadLetterView ToView(QueueMessage message)
    {
        var body = Encoding.UTF8.GetString(message.Body.Span);
        bool truncated = body.Length > MaxBodyPreview;
        if (truncated)
            body = body[..MaxBodyPreview];

        return new DeadLetterView
        {
            MessageId = message.Id,
            QueueName = message.QueueName,
            OriginalQueueName = message.Headers.GetValueOrDefault(MessageHeaders.OriginalQueueName),
            MessageType = message.Headers.GetValueOrDefault(MessageHeaders.MessageType),
            Reason = message.Headers.GetValueOrDefault(MessageHeaders.DeadLetterReason),
            DeadLetteredAt = DateTimeOffset.TryParse(message.Headers.GetValueOrDefault(MessageHeaders.DeadLetteredAt), out var at) ? at : null,
            Attempts = int.TryParse(message.Headers.GetValueOrDefault(MessageHeaders.DeadLetterDequeueCount), out var attempts) ? attempts : null,
            JobId = message.Headers.GetValueOrDefault(MessageHeaders.JobId),
            CorrelationId = message.Headers.GetValueOrDefault(MessageHeaders.CorrelationId),
            Headers = new Dictionary<string, string>(message.Headers),
            BodyTruncated = truncated,
            Body = body
        };
    }
}
