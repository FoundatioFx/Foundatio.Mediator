using ConsoleSample.Messages;
using Foundatio.Mediator.Queues;
using Microsoft.Extensions.Logging;

namespace ConsoleSample.Handlers;

[Queue(Concurrency = 2)]
public class ReportHandler
{
    private readonly ILogger<ReportHandler> _logger;

    public ReportHandler(ILogger<ReportHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(GenerateReport message, CancellationToken ct)
    {
        _logger.LogInformation("📊 Starting report generation: {ReportName} ({ItemCount} items)",
            message.ReportName, message.ItemCount);

        Console.WriteLine($"📊 [Queue Worker] Generating report: {message.ReportName}");

        for (int i = 1; i <= message.ItemCount; i++)
        {
            ct.ThrowIfCancellationRequested();

            int progress = (int)((double)i / message.ItemCount * 100);

            // Simulate work
            await Task.Delay(200, ct);

            Console.WriteLine($"📊 [Queue Worker]   Item {i}/{message.ItemCount} processed ({progress}%)");
        }

        Console.WriteLine($"📊 [Queue Worker] Report '{message.ReportName}' completed successfully!");
    }
}
