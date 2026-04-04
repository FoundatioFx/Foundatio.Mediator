using System.Diagnostics;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;
using Microsoft.Extensions.Logging;

namespace Common.Module.Middleware;

/// <summary>
/// Consolidated middleware that provides logging, performance monitoring, and error handling
/// for all messages across all modules. This middleware is discovered via cross-assembly
/// metadata scanning because the Common.Module assembly is marked with [assembly: FoundatioModule].
///
/// Consolidating these concerns into a single middleware:
/// - Reduces overhead from multiple middleware invocations
/// - Provides consistent, correlated logging
/// - Simplifies the middleware pipeline
/// </summary>
[Middleware(OrderBefore = [typeof(ValidationMiddleware)])]
public class ObservabilityMiddleware
{
    private const long SlowHandlerThresholdMs = 100;

    public Stopwatch Before(object message, HandlerExecutionInfo info, QueueContext? queueContext, ILogger<IMediator> logger)
    {
        var source = queueContext is not null
            ? "queue"
            : message is IDistributedNotification
                ? "distributed event"
                : "local";

        logger.LogInformation(
            "Handling {MessageType} in {HandlerType} (source: {Source})",
            message.GetType().Name,
            info.HandlerType.Name,
            source);

        return Stopwatch.StartNew();
    }

    public void After(object message, Stopwatch stopwatch, HandlerExecutionInfo info, ILogger<IMediator> logger)
    {
        stopwatch.Stop();

        if (stopwatch.ElapsedMilliseconds > SlowHandlerThresholdMs)
        {
            logger.LogWarning(
                "Slow handler: {HandlerType} took {ElapsedMs}ms to handle {MessageType}",
                info.HandlerType.Name,
                stopwatch.ElapsedMilliseconds,
                message.GetType().Name);
        }
        else
        {
            logger.LogInformation(
                "Completed {MessageType} in {HandlerType} ({ElapsedMs}ms)",
                message.GetType().Name,
                info.HandlerType.Name,
                stopwatch.ElapsedMilliseconds);
        }
    }

    public void Finally(object message, Stopwatch? stopwatch, Exception? exception, ILogger<IMediator> logger)
    {
        // Ensure stopwatch is stopped even if After wasn't called due to exception
        stopwatch?.Stop();

        if (exception != null)
        {
            logger.LogError(
                exception,
                "Error handling {MessageType} after {ElapsedMs}ms",
                message.GetType().Name,
                stopwatch?.ElapsedMilliseconds ?? 0);
        }
    }
}
