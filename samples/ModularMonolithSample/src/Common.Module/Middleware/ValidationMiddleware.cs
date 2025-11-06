using Foundatio.Mediator;
using MiniValidation;

namespace Common.Module.Middleware;

[Middleware(5)]
public static class ValidationMiddleware
{
    public static HandlerResult Before(IValidatable message)
    {
        if (MiniValidator.TryValidate(message, out var errors))
            return HandlerResult.Continue();

        var validationErrors = errors.Select(kvp => new ValidationError(kvp.Key, String.Join(", ", kvp.Value))).ToArray();

        return Result.Invalid(validationErrors);
    }
}

public interface IValidatable { }
