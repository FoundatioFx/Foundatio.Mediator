using ConsoleSample.Services;

namespace ConsoleSample.Middleware;

public class GlobalMiddleware
{
    public (DateTime Date, TimeSpan Time) Before(object message, CancellationToken cancellationToken)
    {
        Console.WriteLine($"🌍 [GlobalMiddleware] Before: Processing message of type {message.GetType().Name}");
        return (DateTime.UtcNow, DateTime.UtcNow.TimeOfDay);
    }

    public async Task After(object message, DateTime start, TimeSpan time, IEmailService emailService, CancellationToken cancellationToken)
    {
        await emailService.SendEmailAsync("test@test.com", $"Hello", $"Message of type {message.GetType().Name} processed successfully at {DateTime.UtcNow}");
        Console.WriteLine($"🌍 [GlobalMiddleware] After: Completed processing {message.GetType().Name} {start}");
    }

    public void Finally(object message, Exception? exception, CancellationToken cancellationToken)
    {
        if (exception != null)
        {
            Console.WriteLine($"🌍 [GlobalMiddleware] Finally: Error processing {message.GetType().Name}: {exception.Message}");
        }
        else
        {
            Console.WriteLine($"🌍 [GlobalMiddleware] Finally: Successfully completed {message.GetType().Name}");
        }
    }
}
