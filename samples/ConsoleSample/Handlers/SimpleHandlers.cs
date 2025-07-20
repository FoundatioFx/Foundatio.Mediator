using ConsoleSample.Messages;

namespace ConsoleSample.Handlers;

// Simple handlers for basic command/query examples
public class PingHandler
{
    public async Task HandleAsync(PingCommand command, CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken); // Simulate some work
        Console.WriteLine($"Ping {command.Id} received!");
    }
}

public class GreetingHandler
{
    public string Handle(GreetingQuery query)
    {
        return $"Hello, {query.Name}!";
    }
}
