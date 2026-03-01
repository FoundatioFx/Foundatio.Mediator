namespace Foundatio.Mediator.Tests.Fixtures;

// Common query message — returns a string
public record Ping(string Message) : IQuery;

// Common void command
public record Echo(string Value) : ICommand;

// Common notification event
public record TestEvent(string Name);
