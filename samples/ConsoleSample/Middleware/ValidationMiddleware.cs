using Foundatio.Mediator;
using MiniValidation;

namespace ConsoleSample.Middleware;

public class ValidationMiddleware
{
    public HandlerResult Before(object message)
    {
        if (!TryValidate(message, out var errors))
        {
            return HandlerResult.ShortCircuit(Result.Invalid(errors));
        }

        return HandlerResult.Continue();
    }
}
