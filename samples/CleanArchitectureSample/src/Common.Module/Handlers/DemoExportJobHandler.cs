using Common.Module.Events;
using Common.Module.Messages;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Microsoft.Extensions.Logging;

namespace Common.Module.Handlers;

/// <summary>
/// A long-running tracked job: reports progress (which also heartbeats the message), observes cancellation
/// requested from the dashboard, and tells the event feed which host ran it.
/// </summary>
[Queue(Group = "exports", TrackProgress = true, TimeoutSeconds = 60, Concurrency = 2, Description = "Simulated export with progress, heartbeat, and cancellation")]
public class DemoExportJobHandler(HostInfo host, ILogger<DemoExportJobHandler> logger)
{
    public async Task<Result> HandleAsync(DemoExportJob message, QueueContext queueContext, TenantContext tenant, IMediator mediator, CancellationToken ct)
    {
        var rng = Random.Shared;

        // Per-job variability so a batch of jobs finishes at different times on different hosts.
        int steps = Math.Max(3, (int)(message.Steps * (0.6 + rng.NextDouble() * 0.8)));
        int baseDelay = Math.Max(100, (int)(message.StepDelayMs * (0.5 + rng.NextDouble())));

        logger.LogInformation("Starting export job {JobId} for {Tenant} on {HostId} ({Steps} steps, ~{Delay}ms each)",
            queueContext.JobId, tenant, host.HostId, steps, baseDelay);

        for (int i = 1; i <= steps; i++)
        {
            ct.ThrowIfCancellationRequested();

            // Result.Error is retryable: the worker abandons the message and the next attempt starts over.
            if (rng.NextDouble() < 0.01)
            {
                logger.LogWarning("Export job {JobId}: simulated transient error on step {Step}", queueContext.JobId, i);
                return Result.Error($"Transient failure on step {i}; attempt {queueContext.DequeueCount} of {queueContext.MaxAttempts}");
            }

            // Result.CriticalError is not: the message is dead-lettered immediately.
            if (rng.NextDouble() < 0.002)
            {
                logger.LogError("Export job {JobId}: simulated critical error on step {Step}", queueContext.JobId, i);
                return Result.CriticalError($"Unrecoverable failure on step {i}");
            }

            int jitter = (int)(baseDelay * (0.3 + rng.NextDouble() * 1.4));
            await Task.Delay(jitter, ct).ConfigureAwait(false);

            int percent = (int)((double)i / steps * 100);
            await queueContext.ReportProgressAsync(percent, $"Step {i} of {steps} on {host.HostId}", ct).ConfigureAwait(false);
        }

        logger.LogInformation("Export job {JobId} completed on {HostId}", queueContext.JobId, host.HostId);

        await mediator.PublishAsync(new DemoJobCompleted(
            queueContext.JobId ?? queueContext.MessageId,
            queueContext.QueueName,
            host.HostId,
            tenant.TenantId), ct).ConfigureAwait(false);

        return Result.Ok();
    }
}
