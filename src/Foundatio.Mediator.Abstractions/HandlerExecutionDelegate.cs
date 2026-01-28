namespace Foundatio.Mediator;

/// <summary>
/// Represents a wrapped handler execution that Execute middleware can invoke.
/// Execute middleware uses this delegate to control handler execution,
/// enabling retry, circuit breaker, timeout, and other resilience patterns.
/// </summary>
public delegate ValueTask<object?> HandlerExecutionDelegate();
