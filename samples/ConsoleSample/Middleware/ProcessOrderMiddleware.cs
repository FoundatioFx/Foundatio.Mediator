using Foundatio.Mediator;
using ConsoleSample.Messages;

namespace ConsoleSample.Services;

public class ProcessOrderMiddleware
{
    public Task<HandlerResult> BeforeAsync(CreateOrder command, CancellationToken cancellationToken)
    {
        Console.WriteLine($"🔸 [ProcessOrderMiddleware] Before: Processing order {command.OrderId}");

        // Add some validation logic
        if (String.IsNullOrWhiteSpace(command.OrderId))
        {
            Console.WriteLine($"🔸 [ProcessOrderMiddleware] Invalid order ID, short-circuiting");
            return Task.FromResult(HandlerResult.ShortCircuit("Invalid order ID"));
        }

        Console.WriteLine($"🔸 [ProcessOrderMiddleware] Validation passed, continuing to handler");
        return Task.FromResult(HandlerResult.Continue($"Validated order {command.OrderId}"));
    }

    public Task AfterAsync(CreateOrder command, object? beforeResult, object? handlerResult, CancellationToken cancellationToken)
    {
        Console.WriteLine($"🔸 [ProcessOrderMiddleware] After: Order processing completed");
        Console.WriteLine($"🔸 [ProcessOrderMiddleware] Before result: {beforeResult}");
        Console.WriteLine($"🔸 [ProcessOrderMiddleware] Handler result: {handlerResult}");
        return Task.CompletedTask;
    }

    public Task FinallyAsync(CreateOrder command, Exception? exception, CancellationToken cancellationToken)
    {
        if (exception != null)
        {
            Console.WriteLine($"🔸 [ProcessOrderMiddleware] Finally: Error occurred - {exception.Message}");
        }
        else
        {
            Console.WriteLine($"🔸 [ProcessOrderMiddleware] Finally: Order {command.OrderId} processing completed successfully");
        }
        return Task.CompletedTask;
    }
}
