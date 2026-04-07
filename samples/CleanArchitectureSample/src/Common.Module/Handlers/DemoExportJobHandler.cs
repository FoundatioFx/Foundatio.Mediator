using Common.Module.Messages;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Microsoft.Extensions.Logging;

namespace Common.Module.Handlers;

/// <summary>
/// A demo queue handler with progress tracking enabled.
/// Simulates a long-running export/report generation job that reports progress
/// and supports cancellation via the queue job state store.
/// </summary>
[Queue(TrackProgress = true, Concurrency = 5, TimeoutSeconds = 10, Group = "exports", Description = "Processes export jobs with progress tracking")]
public class DemoExportJobHandler(ILogger<DemoExportJobHandler> logger)
{
    public async Task<Result> HandleAsync(DemoExportJob message, QueueContext queueContext, CancellationToken ct)
    {
        var rng = Random.Shared;

        // Add per-job variability: ±40% on step count, ±50% on delay
        int steps = Math.Max(3, (int)(message.Steps * (0.6 + rng.NextDouble() * 0.8)));
        int baseDelay = Math.Max(100, (int)(message.StepDelayMs * (0.5 + rng.NextDouble())));

        logger.LogInformation("Starting demo export job ({Steps} steps, ~{Delay}ms each)", steps, baseDelay);

        for (int i = 1; i <= steps; i++)
        {
            ct.ThrowIfCancellationRequested();

            // ~5% chance of a transient error (e.g. network blip, temporary service outage).
            // Returning Result.Error tells the QueueWorker to abandon the message so it can be retried.
            if (rng.NextDouble() < 0.05)
            {
                logger.LogWarning("Demo export: simulated transient error on step {Step}", i);
                return Result.Error($"Transient failure on step {i} — will be retried");
            }

            // ~1% chance of an unrecoverable error (e.g. corrupt data, invalid configuration).
            // Returning Result.CriticalError tells the QueueWorker to dead-letter the message immediately.
            if (rng.NextDouble() < 0.01)
            {
                logger.LogError("Demo export: simulated critical error on step {Step}", i);
                return Result.CriticalError($"Unrecoverable failure on step {i} — will not be retried");
            }

            // Simulate variable work — some steps are fast, some slow
            int jitter = (int)(baseDelay * (0.3 + rng.NextDouble() * 1.4));
            await Task.Delay(jitter, ct).ConfigureAwait(false);

            int percent = (int)((double)i / steps * 100);
            string stepMessage = $"Processing step {i} of {steps}";
            await queueContext.ReportProgressAsync(percent, stepMessage, ct).ConfigureAwait(false);

            logger.LogDebug("Demo export: {Percent}% - {Message}", percent, stepMessage);
        }

        logger.LogInformation("Demo export job completed successfully");
        return Result.Ok();
    }
}
