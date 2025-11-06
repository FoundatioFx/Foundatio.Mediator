using Foundatio.Mediator;
using MiniValidation;

namespace Orders.Module.Middleware;

// NOTE: Middleware must be defined in the same project as handlers.
// To share middleware across projects, use linked files in .csproj:
// <Compile Include="..\Shared\ValidationMiddleware.cs" Link="Middleware\ValidationMiddleware.cs" />
// Declare middleware as 'internal' to avoid type conflicts across assemblies.

[FoundatioOrder(1)]
public static class ValidationMiddleware
{
    public static HandlerResult Before(object message)
    {
        if (MiniValidator.TryValidate(message, out var errors))
            return HandlerResult.Continue();

        var validationErrors = errors.Select(kvp => new ValidationError(kvp.Key, String.Join(", ", kvp.Value))).ToArray();

        return Result.Invalid(validationErrors);
    }
}
