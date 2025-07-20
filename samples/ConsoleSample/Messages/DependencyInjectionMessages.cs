namespace ConsoleSample.Messages;

// Messages for dependency injection examples
public record SendWelcomeEmailCommand(string Email, string Name);
public record CreatePersonalizedGreetingQuery(string Name);
