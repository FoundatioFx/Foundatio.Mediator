using Foundatio.Mediator;
using Microsoft.Extensions.Logging;

namespace Common.Module.Middleware;

/// <summary>
/// Logging middleware that runs for all messages across all modules.
/// This middleware is discovered via cross-assembly metadata scanning because
/// the Common.Module assembly is marked with [assembly: FoundatioModule].
/// </summary>
[Middleware(Order = 1)] // Runs first - logs before validation
public static class LoggingMiddleware
{
    public static void Before(object message, HandlerExecutionInfo info, ILogger<IMediator> logger)
    {
        logger.LogInformation(
            "Handling {MessageType} in {HandlerType}",
            message.GetType().Name,
            info.HandlerType.Name);
    }

    public static void After(object message, HandlerExecutionInfo info, ILogger<IMediator> logger)
    {
        logger.LogInformation(
            "Completed {MessageType} in {HandlerType}",
            message.GetType().Name,
            info.HandlerType.Name);
    }

    public static void Finally(object message, Exception? exception, ILogger<IMediator> logger)
    {
        if (exception != null)
        {
            logger.LogError(exception, "Error handling {MessageType}", message.GetType().Name);
        }
    }
}
