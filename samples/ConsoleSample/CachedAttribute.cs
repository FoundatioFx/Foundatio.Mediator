using ConsoleSample.Middleware;
using Foundatio.Mediator;

namespace ConsoleSample;

/// <summary>
/// Specifies that the handler method should cache its response.
/// Subsequent calls with the same message will return the cached value without executing the handler.
/// The message type must implement value equality (records work automatically).
/// </summary>
/// <example>
/// <code>
/// [Cached(DurationSeconds = 60)]
/// public Result&lt;Order&gt; Handle(GetOrder query) { ... }
/// </code>
/// </example>
[UseMiddleware(typeof(CachingMiddleware))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class CachedAttribute : Attribute
{

    /// <summary>
    /// Cache duration in seconds. Default is 300 (5 minutes).
    /// </summary>
    public int DurationSeconds { get; set; } = 300;

    /// <summary>
    /// Whether to use a sliding expiration (resets on each access). Default is false (absolute expiration).
    /// </summary>
    public bool SlidingExpiration { get; set; }
}
