using ConsoleSample.Middleware;
using Foundatio.Mediator;

namespace ConsoleSample;

/// <summary>
/// Specifies that the handler method or class should use retry logic on transient failures.
/// Apply to handler methods or classes to enable automatic retries with configurable settings.
/// </summary>
/// <example>
/// <code>
/// [Retry(MaxAttempts = 5, DelayMs = 200)]
/// public Result&lt;Order&gt; Handle(CreateOrder command) { ... }
/// </code>
/// </example>
[UseMiddleware(typeof(RetryMiddleware))]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RetryAttribute : Attribute
{
    /// <summary>
    /// Maximum number of retry attempts. Default is 3.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Initial delay in milliseconds between retries. Default is 100ms.
    /// When using exponential backoff, this is the base delay.
    /// </summary>
    public int DelayMs { get; set; } = 100;

    /// <summary>
    /// Whether to use exponential backoff for retry delays. Default is true.
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;

    /// <summary>
    /// Whether to add jitter to retry delays to avoid thundering herd. Default is true.
    /// </summary>
    public bool UseJitter { get; set; } = true;
}
