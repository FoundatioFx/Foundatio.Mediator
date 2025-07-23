using Foundatio.Mediator;
using MiniValidation;

namespace ConsoleSample.Middleware;

/// <summary>
/// Validation middleware that uses MiniValidator to validate message models before handler execution.
/// If validation fails, it short-circuits and returns validation errors as a Result.
/// </summary>
public class ValidationMiddleware
{
    /// <summary>
    /// Validates the message before handler execution. If validation fails, returns a short-circuit result.
    /// </summary>
    /// <param name="message">The message to validate.</param>
    /// <returns>A HandlerResult that either continues or short-circuits with validation errors.</returns>
    public HandlerResult Before(object message)
    {
        if (!MiniValidator.TryValidate(message, out var errors))
        {
            // Convert MiniValidator errors to Foundatio.Mediator ValidationErrors
            var validationErrors = errors.SelectMany(kvp =>
                kvp.Value.Select(errorMessage =>
                    new ValidationError(kvp.Key, errorMessage))).ToList();

            return HandlerResult.ShortCircuit(Result.Invalid(validationErrors));
        }

        return HandlerResult.Continue();
    }
}
