namespace Foundatio.Mediator;

/// <summary>
/// Implement this interface on an attribute to customize endpoint or group builders at startup.
/// The source generator detects attributes implementing this interface and emits code to
/// instantiate the attribute and call <see cref="Configure"/> with the builder.
/// </summary>
/// <typeparam name="TBuilder">
/// The builder type to configure. Use <c>RouteHandlerBuilder</c> for individual endpoints
/// or <c>RouteGroupBuilder</c> for endpoint groups.
/// Both implement <c>IEndpointConventionBuilder</c>, which can also be used as a common target.
/// </typeparam>
/// <example>
/// <code>
/// [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
/// public class RateLimitedAttribute : Attribute, IEndpointConvention&lt;RouteHandlerBuilder&gt;
/// {
///     public string PolicyName { get; set; } = "fixed";
///
///     public void Configure(RouteHandlerBuilder builder)
///     {
///         builder.RequireRateLimiting(PolicyName);
///     }
/// }
/// </code>
/// </example>
public interface IEndpointConvention<in TBuilder>
{
    /// <summary>
    /// Configures the endpoint or group builder.
    /// Called once at application startup during endpoint registration.
    /// </summary>
    /// <param name="builder">The endpoint or group builder to configure.</param>
    void Configure(TBuilder builder);
}
