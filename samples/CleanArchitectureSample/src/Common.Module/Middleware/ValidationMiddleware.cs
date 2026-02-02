using System.Collections.Concurrent;
using Foundatio.Mediator;
using MiniValidation;

namespace Common.Module.Middleware;

/// <summary>
/// Apply this attribute to a message to skip validation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class SkipValidationAttribute : Attribute { }

[Middleware(5)]
public static class ValidationMiddleware
{
    private static readonly ConcurrentDictionary<Type, bool> _skipValidationCache = new();

    public static HandlerResult Before(object message)
    {
        // Skip validation if the message is decorated with [SkipValidation] (memoized)
        if (_skipValidationCache.GetOrAdd(message.GetType(), static t =>
            t.GetCustomAttributes(typeof(SkipValidationAttribute), false).Length > 0))
            return HandlerResult.Continue();

        if (MiniValidator.TryValidate(message, out var errors))
            return HandlerResult.Continue();

        var validationErrors = errors.Select(kvp => new ValidationError(kvp.Key, String.Join(", ", kvp.Value))).ToArray();

        return Result.Invalid(validationErrors);
    }
}
