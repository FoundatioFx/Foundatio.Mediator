using Foundatio.Mediator;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ConsoleSample.Middleware;

[FoundatioOrder(2)]
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
    public Stopwatch Before(object message)
    {
        var stopwatch = Stopwatch.StartNew();
        return stopwatch;
    }

    /// <summary>
    /// Called always, even if handler fails - ensures cleanup and error logging
    /// </summary>
    public void Finally(object message, Stopwatch stopwatch, Exception? exception)
    {
        stopwatch?.Stop();

        if (exception != null)
        {
            _logger.LogError(exception, "❌ Failed {MessageType} handler after {ElapsedMs}ms",
                message.GetType().Name, stopwatch?.ElapsedMilliseconds ?? 0);

            Console.WriteLine($"❌ Failed {message.GetType().Name} after {stopwatch?.ElapsedMilliseconds ?? 0}ms: {exception.Message}");
        }
        else
        {
            _logger.LogDebug("🏁 Finished {MessageType} handler execution", message.GetType().Name);
        }
    }
}
