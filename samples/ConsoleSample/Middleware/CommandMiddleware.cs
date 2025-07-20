using Foundatio.Mediator;

namespace ConsoleSample.Middleware;

public class CommandMiddleware
{
    public Task<HandlerResult> BeforeAsync(ICommand command, CancellationToken cancellationToken)
    {
        Console.WriteLine($"📋 [CommandMiddleware] Before: Processing command of type {command.GetType().Name}");
        return Task.FromResult(HandlerResult.Continue());
    }
}
