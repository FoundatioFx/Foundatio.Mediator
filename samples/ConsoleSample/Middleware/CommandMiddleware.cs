using Foundatio.Mediator;

namespace ConsoleSample.Middleware;

public class CommandMiddleware
{
    public void Before(ICommand command)
    {
        Console.WriteLine($"📋 [CommandMiddleware] Before: Processing command of type {command.GetType().Name}");
    }
}
