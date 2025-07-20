namespace ConsoleSample.Messages;

// Simple messages for basic command/query examples
public record PingCommand(string Id);
public record GreetingQuery(string Name);
