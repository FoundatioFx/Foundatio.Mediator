namespace Foundatio.Mediator;

/// <summary>
/// Represents the result of handler execution that can be used to control execution flow.
/// </summary>
public readonly struct HandlerResult : IEquatable<HandlerResult>
{
    private HandlerResult(object? value, bool isShortCircuited, object? replacementMessage = null)
    {
        Value = value;
        IsShortCircuited = isShortCircuited;
        ReplacementMessage = replacementMessage;
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
    /// The message to dispatch in place of the current message, if any.
    /// Set via <see cref="ContinueWith"/>.
    /// </summary>
    public object? ReplacementMessage { get; }

    /// <summary>
    /// Creates a result that indicates normal execution should continue.
    /// </summary>
    /// <param name="value">Optional value to pass to After/Finally middleware.</param>
    /// <returns>A handler result that allows continued execution.</returns>
    public static HandlerResult Continue(object? value = null) => new(value, false);

    /// <summary>
    /// Creates a result that continues execution with a replacement message. The rest of the
    /// pipeline — subsequent middleware, the handler, and After/Finally methods — receives the
    /// replacement instead of the original message. Use this to enrich immutable messages
    /// (e.g. a record <c>with</c> expression stamping tenant or user context).
    /// </summary>
    /// <param name="message">The message to dispatch. Must be of the same type as the original message.</param>
    /// <param name="value">Optional value to pass to After/Finally middleware.</param>
    /// <returns>A handler result that continues execution with the replacement message.</returns>
    public static HandlerResult ContinueWith(object message, object? value = null)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));

        return new(value, false, message);
    }

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

    /// <inheritdoc />
    public bool Equals(HandlerResult other) =>
        IsShortCircuited == other.IsShortCircuited && Equals(Value, other.Value) && Equals(ReplacementMessage, other.ReplacementMessage);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is HandlerResult other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hashCode = (Value?.GetHashCode() ?? 0) * 397;
            hashCode = (hashCode ^ IsShortCircuited.GetHashCode()) * 397;
            return hashCode ^ (ReplacementMessage?.GetHashCode() ?? 0);
        }
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(HandlerResult left, HandlerResult right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(HandlerResult left, HandlerResult right) => !left.Equals(right);
}

/// <summary>
/// Represents a strongly-typed result of handler execution that can be used to control execution flow.
/// This generic version avoids boxing for value types and provides type safety.
/// </summary>
public readonly struct HandlerResult<T> : IEquatable<HandlerResult<T>>
{
    private HandlerResult(T value, bool isShortCircuited, object? replacementMessage = null)
    {
        Value = value;
        IsShortCircuited = isShortCircuited;
        ReplacementMessage = replacementMessage;
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
    /// The message to dispatch in place of the current message, if any.
    /// Set via <see cref="ContinueWith"/>.
    /// </summary>
    public object? ReplacementMessage { get; }

    /// <summary>
    /// Creates a result that indicates normal execution should continue.
    /// </summary>
    /// <param name="value">Optional value to pass to After/Finally middleware.</param>
    /// <returns>A handler result that allows continued execution.</returns>
    public static HandlerResult<T> Continue(T value = default!) => new(value, false);

    /// <summary>
    /// Creates a result that continues execution with a replacement message. The rest of the
    /// pipeline — subsequent middleware, the handler, and After/Finally methods — receives the
    /// replacement instead of the original message.
    /// </summary>
    /// <param name="message">The message to dispatch. Must be of the same type as the original message.</param>
    /// <param name="value">Optional value to pass to After/Finally middleware.</param>
    /// <returns>A handler result that continues execution with the replacement message.</returns>
    public static HandlerResult<T> ContinueWith(object message, T value = default!)
    {
        if (message is null)
            throw new ArgumentNullException(nameof(message));

        return new(value, false, message);
    }

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
    public HandlerResult ToNonGeneric()
    {
        if (IsShortCircuited)
            return HandlerResult.ShortCircuit((object?)Value);

        return ReplacementMessage is null
            ? HandlerResult.Continue((object?)Value)
            : HandlerResult.ContinueWith(ReplacementMessage, (object?)Value);
    }

    /// <summary>
    /// Implicitly converts from generic to non-generic HandlerResult.
    /// </summary>
    public static implicit operator HandlerResult(HandlerResult<T> result) => result.ToNonGeneric();

    /// <inheritdoc />
    public bool Equals(HandlerResult<T> other) =>
        IsShortCircuited == other.IsShortCircuited && EqualityComparer<T>.Default.Equals(Value, other.Value) && Equals(ReplacementMessage, other.ReplacementMessage);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is HandlerResult<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hashCode = EqualityComparer<T>.Default.GetHashCode(Value!) * 397;
            hashCode = (hashCode ^ IsShortCircuited.GetHashCode()) * 397;
            return hashCode ^ (ReplacementMessage?.GetHashCode() ?? 0);
        }
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(HandlerResult<T> left, HandlerResult<T> right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(HandlerResult<T> left, HandlerResult<T> right) => !left.Equals(right);
}
