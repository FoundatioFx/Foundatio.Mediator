using Foundatio.Messaging;
using Common.Module.Events;
using Common.Module.Messages;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Microsoft.Extensions.Logging;

namespace Common.Module.Handlers;

/// <summary>
/// A tracked job on its own worker group. Concurrency stays at 1, so several imports queue up behind each
/// other on a single imports worker and the dashboard shows the backlog.
/// </summary>
[Queue(DisplayName = "Product catalog imports", Group = "imports", TrackProgress = true, TimeoutSeconds = 60, Description = "Simulated catalog import; one file at a time per worker")]
public class ImportProductCatalogHandler(HostInfo host, ILogger<ImportProductCatalogHandler> logger)
{
    [HandlerEndpoint(Exclude = true)]
    public async Task<Result> HandleAsync(ImportProductCatalog message, MessageProcessingContext context, TenantContext tenant, IMediator mediator, CancellationToken ct)
    {
        int rows = Math.Clamp(message.Rows, 1, 10_000);
        int rowDelay = Math.Clamp(message.RowDelayMs, 0, 5_000);
        int reportEvery = Math.Max(1, rows / 20);

        logger.LogInformation("Starting catalog import {JobId} for {Tenant} on {HostId} ({Rows} rows)",
            context.JobId, tenant, host.HostId, rows);

        for (int row = 1; row <= rows; row++)
        {
            await Task.Delay(rowDelay, ct).ConfigureAwait(false);

            if (row % reportEvery == 0 || row == rows)
                await context.ReportProgressAsync(row * 100 / rows, $"Imported {row} of {rows} rows on {host.HostId}", ct).ConfigureAwait(false);
        }

        logger.LogInformation("Catalog import {JobId} completed on {HostId}", context.JobId, host.HostId);

        await mediator.PublishAsync(new DemoJobCompleted(
            context.JobId ?? context.MessageId,
            context.QueueName,
            host.HostId,
            tenant.TenantId), ct).ConfigureAwait(false);

        return Result.Ok();
    }
}
