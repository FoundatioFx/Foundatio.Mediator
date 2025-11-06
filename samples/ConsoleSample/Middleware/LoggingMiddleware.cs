using Foundatio.Mediator;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ConsoleSample.Middleware;

[Middleware(2)]
public class LoggingMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(ILogger<LoggingMiddleware> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Called before handler execution - starts timing and logs entry
    /// </summary>
    public Stopwatch Before(object message, HandlerExecutionInfo handlerInfo)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogDebug("▶️  Executing {HandlerType}.{HandlerMethod} for {MessageType}",
            handlerInfo.HandlerType.Name, handlerInfo.HandlerMethod.Name, message.GetType().Name);
        return stopwatch;
    }

    /// <summary>
    /// Called always, even if handler fails - ensures cleanup and error logging
    /// </summary>
    public void Finally(object message, HandlerExecutionInfo handlerInfo, Stopwatch stopwatch, Exception? exception)
    {
        stopwatch?.Stop();

        if (exception != null)
        {
            _logger.LogError(exception, "❌ Failed {HandlerType}.{HandlerMethod} for {MessageType} after {ElapsedMs}ms",
                handlerInfo.HandlerType.Name, handlerInfo.HandlerMethod.Name, message.GetType().Name, stopwatch?.ElapsedMilliseconds ?? 0);

            Console.WriteLine($"❌ Failed {handlerInfo.HandlerType.Name}.{handlerInfo.HandlerMethod.Name} after {stopwatch?.ElapsedMilliseconds ?? 0}ms: {exception.Message}");
        }
        else
        {
            _logger.LogDebug("🏁 Finished {HandlerType}.{HandlerMethod} execution", handlerInfo.HandlerType.Name, handlerInfo.HandlerMethod.Name);
        }
    }
}
