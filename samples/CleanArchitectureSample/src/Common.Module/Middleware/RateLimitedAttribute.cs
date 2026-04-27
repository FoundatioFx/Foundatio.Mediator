using Foundatio.Mediator;
using Microsoft.AspNetCore.Builder;

namespace Common.Module.Middleware;

/// <summary>
/// Applies a rate limiting policy to the generated endpoint.
/// This attribute uses <see cref="IEndpointConvention{TBuilder}"/> to configure
/// the endpoint builder at startup — no runtime reflection needed.
/// </summary>
/// <example>
/// <code>
/// // Apply a named rate limiting policy
/// [RateLimited("fixed")]
/// public Result&lt;Product&gt; Handle(GetProduct query) { ... }
///
/// // Apply the default policy
/// [RateLimited]
/// public Result&lt;List&lt;Order&gt;&gt; Handle(GetOrders query) { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RateLimitedAttribute : Attribute, IEndpointConvention<RouteHandlerBuilder>
{
    /// <summary>
    /// Creates a new <see cref="RateLimitedAttribute"/> with an optional policy name.
    /// </summary>
    /// <param name="policyName">The rate limiting policy name. When null, applies the default policy.</param>
    public RateLimitedAttribute(string? policyName = null)
    {
        PolicyName = policyName;
    }

    /// <summary>
    /// The rate limiting policy name configured in <c>AddRateLimiter()</c>.
    /// When null, the default rate limiting policy is applied.
    /// </summary>
    public string? PolicyName { get; }

    /// <inheritdoc />
    public void Configure(RouteHandlerBuilder builder)
    {
        if (PolicyName is not null)
            builder.RequireRateLimiting(PolicyName);
        else
            builder.RequireRateLimiting("default");
    }
}
