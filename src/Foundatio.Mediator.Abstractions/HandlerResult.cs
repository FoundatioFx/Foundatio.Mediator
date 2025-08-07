namespace Foundatio.Mediator;

/// <summary>
/// Represents the result of handler execution that can be used to control execution flow.
/// </summary>
public struct HandlerResult
{
    private HandlerResult(object? value, bool isShortCircuited)
    {
        Value = value;
        IsShortCircuited = isShortCircuited;
    }

    /// <summary>
    /// The result value, if any.
    /// </summary>
    public object? Value { get; }

    /// <summary>
    /// Whether the handler execution should be short-circuited.
    /// </summary>
    public bool IsShortCircuited { get; }

    /// <summary>
    /// Creates a result that indicates normal execution should continue.
    /// </summary>
    /// <param name="value">Optional value to pass to After/Finally middleware.</param>
    /// <returns>A handler result that allows continued execution.</returns>
    public static HandlerResult Continue(object? value = null) => new(value, false);

    /// <summary>
    /// Creates a result that short-circuits handler execution.
    /// </summary>
    /// <param name="value">The value to return as the handler result.</param>
    /// <returns>A handler result that short-circuits execution.</returns>
    public static HandlerResult ShortCircuit(object? value = null) => new(value, true);

    /// <summary>
    /// Implicitly converts any value to a short-circuited HandlerResult.
    /// </summary>
    /// <param name="value">The value to short-circuit with.</param>
    /// <returns>A short-circuited HandlerResult containing the value.</returns>
    public static implicit operator HandlerResult(Result value) => ShortCircuit(value);
}
