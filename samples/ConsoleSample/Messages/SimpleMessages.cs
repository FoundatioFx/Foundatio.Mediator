using Foundatio.Mediator;

namespace ConsoleSample.Messages;

// Simple messages for basic command/query examples
public record PingCommand(string Id) : ICommand
{
    public string CommandId => Id;
}

public record GreetingQuery(string Name);
