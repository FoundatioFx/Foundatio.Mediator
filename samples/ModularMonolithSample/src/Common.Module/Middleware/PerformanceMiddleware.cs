using System.Diagnostics;
using Foundatio.Mediator;
using Microsoft.Extensions.Logging;

namespace Common.Module.Middleware;

/// <summary>
/// Performance monitoring middleware that tracks execution time for all messages.
/// Uses cross-assembly middleware discovery based on naming convention.
/// </summary>
public class PerformanceMiddleware
{
    public Stopwatch Before(object message, HandlerExecutionInfo info)
    {
        return Stopwatch.StartNew();
    }

    public void Finally(object message, Stopwatch stopwatch, HandlerExecutionInfo info, ILogger<IMediator> logger)
    {
        stopwatch.Stop();

        if (stopwatch.ElapsedMilliseconds > 100)
        {
            logger.LogWarning(
                "Slow handler detected: {HandlerType} took {ElapsedMs}ms to handle {MessageType}",
                info.HandlerType.Name,
                stopwatch.ElapsedMilliseconds,
                message.GetType().Name);
        }
        else
        {
            logger.LogDebug(
                "{HandlerType} handled {MessageType} in {ElapsedMs}ms",
                info.HandlerType.Name,
                message.GetType().Name,
                stopwatch.ElapsedMilliseconds);
        }
    }
}
