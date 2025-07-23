using ConsoleSample.Messages;
using ConsoleSample.Services;
using Microsoft.Extensions.Logging;

namespace ConsoleSample.Handlers;

// Handlers with dependency injection examples
public class SendWelcomeEmailHandler
{
    public async Task HandleAsync(
        SendWelcomeEmailCommand command, 
        IEmailService emailService,
        IGreetingService greetingService,
        CancellationToken cancellationToken = default)
    {
        string greeting = greetingService.CreateGreeting(command.Name);
        await emailService.SendEmailAsync(
            command.Email, 
            "Welcome!", 
            greeting);
    }
}

public class CreatePersonalizedGreetingHandler
{
    public async Task<string> HandleAsync(
        CreatePersonalizedGreetingQuery query,
        IGreetingService greetingService,
        ILogger<CreatePersonalizedGreetingHandler> logger,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Creating personalized greeting for {Name}", query.Name);
        await Task.CompletedTask;
        return greetingService.CreateGreeting(query.Name);
    }
}
