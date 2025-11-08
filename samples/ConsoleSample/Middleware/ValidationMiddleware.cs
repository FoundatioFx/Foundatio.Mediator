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
                new ValidationError(kvp.Key, string.Join(", ", kvp.Value)))
                .ToArray();

            // short-circuit with validation errors
            // result is implicitly converted to a short-circuited HandlerResult
            return Result.Invalid(validationErrors);
        }

        // continue
        return HandlerResult.Continue();
    }
}
