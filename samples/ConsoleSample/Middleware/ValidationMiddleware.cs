using Foundatio.Mediator;
using MiniValidation;

namespace ConsoleSample.Middleware;

[FoundatioOrder(1)]
public static class ValidationMiddleware
{
    public static Result? Before(object message)
    {
        if (!MiniValidator.TryValidate(message, out var errors))
        {
            var validationErrors = errors.Select(kvp =>
                new ValidationError(kvp.Key, string.Join(", ", kvp.Value)))
                .ToArray();

            // short-circuit with validation errors
            return Result.Invalid(validationErrors);
        }

        // continue
        return null;
    }
}
