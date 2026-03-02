namespace Foundatio.Mediator;

/// <summary>
/// Specifies that a handler class or method allows anonymous (unauthenticated) access,
/// overriding any <see cref="HandlerAuthorizeAttribute"/> on the class or assembly-level
/// <c>AuthorizationRequired</c> setting.
/// <para>
/// Both this attribute and ASP.NET Core's <c>[AllowAnonymous]</c> are recognized.
/// Use this attribute in projects that don't reference ASP.NET Core.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [HandlerAuthorize] // Class-level auth required
/// public class OrderHandler
/// {
///     public Result&lt;Order&gt; Handle(GetOrder query) { ... }  // Requires auth
///
///     [HandlerAllowAnonymous] // Override: this method allows anonymous access
///     public Result&lt;List&lt;Order&gt;&gt; Handle(GetPublicOrders query) { ... }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class HandlerAllowAnonymousAttribute : Attribute
{
}
