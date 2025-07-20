using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace ConsoleSample.Middleware;

public class LoggingMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(ILogger<LoggingMiddleware> logger)
    {
        _logger = logger;
    }

    public Stopwatch Before(object message)
    {
        Console.WriteLine($"🔸 [LoggingMiddleware] Before: Message with type {message.GetType().Name}");
        _logger.LogInformation("[LoggingMiddleware] Before: Message with type {MessageType}", message.GetType().Name);
        return Stopwatch.StartNew();
    }

    public void Finally(object message, Stopwatch stopwatch, Exception? exception)
    {
        stopwatch.Stop();
        if (exception != null)
        {
            Console.WriteLine($"🔸 [LoggingMiddleware] Finally: Error processing {message.GetType().Name}: {exception.Message}");
            _logger.LogError(exception, "[LoggingMiddleware] Finally: Error processing {MessageType}", message.GetType().Name);
        }
        else
        {
            Console.WriteLine($"🔸 [LoggingMiddleware] Finally: Successfully completed {message.GetType().Name} in {stopwatch.ElapsedMilliseconds} ms");
            _logger.LogInformation("[LoggingMiddleware] Finally: Successfully completed {MessageType} in {ElapsedMilliseconds} ms", message.GetType().Name, stopwatch.ElapsedMilliseconds);
        }
    }
}
