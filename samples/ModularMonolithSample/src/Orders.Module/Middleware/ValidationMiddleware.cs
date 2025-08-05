using Foundatio.Mediator;
using MiniValidation;

namespace Orders.Module.Middleware;

[FoundatioOrder(1)]
public static class ValidationMiddleware
{
    public static HandlerResult Before(object message)
    {
        if (MiniValidator.TryValidate(message, out var errors))
            return HandlerResult.Continue();

        var validationErrors = errors.Select(kvp => new ValidationError(kvp.Key, String.Join(", ", kvp.Value))).ToArray();

        return HandlerResult.ShortCircuit(Result.Invalid(validationErrors));
    }
}
