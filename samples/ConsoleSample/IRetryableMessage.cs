namespace ConsoleSample;

/// <summary>
/// Marker interface for messages that should use retry middleware.
/// Apply this to message types that may need automatic retry on transient failures.
/// The retry behavior can be customized via [Retryable] attribute on the handler.
/// </summary>
public interface IRetryableMessage { }
