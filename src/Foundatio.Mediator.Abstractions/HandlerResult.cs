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
    /// Creates a strongly-typed result that short-circuits handler execution.
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="value">The value to return as the handler result.</param>
    /// <returns>A strongly-typed handler result that short-circuits execution.</returns>
    public static HandlerResult<T> ShortCircuit<T>(T value) => HandlerResult<T>.ShortCircuit(value);

    /// <summary>
    /// Implicitly converts any value to a short-circuited HandlerResult.
    /// </summary>
    /// <param name="value">The value to short-circuit with.</param>
    /// <returns>A short-circuited HandlerResult containing the value.</returns>
    public static implicit operator HandlerResult(Result value) => ShortCircuit(value);
}

/// <summary>
/// Represents a strongly-typed result of handler execution that can be used to control execution flow.
/// This generic version avoids boxing for value types and provides type safety.
/// </summary>
public struct HandlerResult<T>
{
    private HandlerResult(T value, bool isShortCircuited)
    {
        Value = value;
        IsShortCircuited = isShortCircuited;
    }

    /// <summary>
    /// The result value.
    /// </summary>
    public T Value { get; }

    /// <summary>
    /// Whether the handler execution should be short-circuited.
    /// </summary>
    public bool IsShortCircuited { get; }

    /// <summary>
    /// Creates a result that indicates normal execution should continue.
    /// </summary>
    /// <param name="value">Optional value to pass to After/Finally middleware.</param>
    /// <returns>A handler result that allows continued execution.</returns>
    public static HandlerResult<T> Continue(T value = default!) => new(value, false);

    /// <summary>
    /// Creates a result that short-circuits handler execution.
    /// </summary>
    /// <param name="value">The value to return as the handler result.</param>
    /// <returns>A handler result that short-circuits execution.</returns>
    public static HandlerResult<T> ShortCircuit(T value) => new(value, true);

    /// <summary>
    /// Converts to non-generic HandlerResult.
    /// </summary>
    /// <remarks>
    /// Explicitly casts Value to object to avoid overload resolution picking the generic
    /// ShortCircuit&lt;T&gt; method, which would cause infinite recursion through the implicit
    /// operator when T happens to be a type that matches the generic overload better than object.
    /// </remarks>
    public HandlerResult ToNonGeneric() => IsShortCircuited
        ? HandlerResult.ShortCircuit((object?)Value)
        : HandlerResult.Continue((object?)Value);

    /// <summary>
    /// Implicitly converts from generic to non-generic HandlerResult.
    /// </summary>
    public static implicit operator HandlerResult(HandlerResult<T> result) => result.ToNonGeneric();
}
