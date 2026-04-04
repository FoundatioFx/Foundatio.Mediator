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
        logger.LogInformation("Starting demo export job ({Steps} steps, {Delay}ms each)", message.Steps, message.StepDelayMs);

        for (int i = 1; i <= message.Steps; i++)
        {
            ct.ThrowIfCancellationRequested();

            // Simulate work
            await Task.Delay(message.StepDelayMs, ct).ConfigureAwait(false);

            int percent = (int)((double)i / message.Steps * 100);
            string stepMessage = $"Processing step {i} of {message.Steps}";
            await queueContext.ReportProgressAsync(percent, stepMessage, ct).ConfigureAwait(false);

            logger.LogDebug("Demo export: {Percent}% - {Message}", percent, stepMessage);
        }

        logger.LogInformation("Demo export job completed successfully");
        return Result.Ok();
    }
}
