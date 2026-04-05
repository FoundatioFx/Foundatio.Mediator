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
[Queue(TrackProgress = true, Concurrency = 5)]
public class DemoExportJobHandler(ILogger<DemoExportJobHandler> logger)
{
    public async Task<Result> HandleAsync(DemoExportJob message, QueueContext queueContext, CancellationToken ct)
    {
        // Add per-job variability: ±40% on step count, ±50% on delay
        var rng = Random.Shared;
        int steps = Math.Max(3, (int)(message.Steps * (0.6 + rng.NextDouble() * 0.8)));
        int baseDelay = Math.Max(100, (int)(message.StepDelayMs * (0.5 + rng.NextDouble())));

        logger.LogInformation("Starting demo export job ({Steps} steps, ~{Delay}ms each)", steps, baseDelay);

        for (int i = 1; i <= steps; i++)
        {
            ct.ThrowIfCancellationRequested();

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
