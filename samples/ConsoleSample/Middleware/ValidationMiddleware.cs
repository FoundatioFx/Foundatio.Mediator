using ConsoleSample.Messages;
using Foundatio.Mediator;
using Microsoft.Extensions.Logging;
using MiniValidation;

namespace ConsoleSample.Middleware;

[Middleware(Order = 1)]
public static class ValidationMiddleware
{
    public static HandlerResult Before(IValidatable message, ILogger<IMediator> logger)
    {
        logger.LogInformation("Validating message of type {MessageType}", message.GetType().Name);

        if (!MiniValidator.TryValidate(message, out var errors))
        {
            var validationErrors = errors.Select(kvp =>
                ValidationError.Create(kvp.Key, string.Join(", ", kvp.Value)))
                .ToArray();

            return HandlerResult.ShortCircuit(Result.Invalid(validationErrors));
        }

        // continue
        return HandlerResult.Continue();
    }
}
