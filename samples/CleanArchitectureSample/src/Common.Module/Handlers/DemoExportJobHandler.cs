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
[Queue(Group = "exports", TrackProgress = true, MaxAttempts = 3, RetryDelays = "3s,6s", TimeoutSeconds = 60, Concurrency = 2, Description = "Simulated export with progress, heartbeat, and cancellation")]
public class DemoExportJobHandler(HostInfo host, ILogger<DemoExportJobHandler> logger)
{
    public async Task<Result> HandleAsync(DemoExportJob message, QueueContext queueContext, TenantContext tenant, IMediator mediator, CancellationToken ct)
    {
        int steps = message.Steps;
        logger.LogInformation("Starting export job {JobId} for {Tenant} on {HostId} ({Steps} steps)",
            queueContext.JobId, tenant, host.HostId, steps);

        for (int i = 1; i <= steps; i++)
        {
            await Task.Delay(message.StepDelayMs, ct).ConfigureAwait(false);
            if (i == Math.Min(2, steps) && message.CriticalFailure)
                return Result.CriticalError("Simulated unrecoverable export failure; inspect the dead letter before replaying.");
            if (i == Math.Min(2, steps) && queueContext.DequeueCount <= message.FailTimes)
                return Result.Error($"Simulated transient failure on attempt {queueContext.DequeueCount}; retry starts from the beginning.");

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
