using Foundatio.Mediator;
using MiniValidation;

namespace ConsoleSample.Middleware;

[FoundatioOrder(1)]
public static class ValidationMiddleware
{
    public static HandlerResult Before(object message)
    {
        if (!MiniValidator.TryValidate(message, out var errors))
        {
            var validationErrors = errors.Select(kvp =>
                new ValidationError(kvp.Key, string.Join(", ", kvp.Value)))
                .ToArray();

            return HandlerResult.ShortCircuit(Result.Invalid(validationErrors));
        }

        return HandlerResult.Continue();
    }
}
