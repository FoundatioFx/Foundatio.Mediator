namespace ConsoleSample;

/// <summary>
/// Marker interface for messages that should be retried on transient failures.
/// Messages implementing this interface will have the RetryMiddleware applied,
/// which wraps the entire pipeline with retry logic using Foundatio Resilience.
/// </summary>
public interface IRetryableMessage { }
