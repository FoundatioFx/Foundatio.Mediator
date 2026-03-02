namespace Foundatio.Mediator;

/// <summary>
/// Specifies that a handler class or method requires authorization.
/// The mere presence of this attribute implies authorization is required — no explicit
/// <c>Required</c> property is needed.
/// <para>
/// Authorization is enforced both for HTTP endpoints (via <c>.RequireAuthorization()</c>) and
/// for direct mediator calls (via generated authorization checks in handler wrappers).
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Require authentication (no specific roles/policies)
/// [HandlerAuthorize]
/// public class OrderHandler { ... }
///
/// // Require specific roles (any-of semantics)
/// [HandlerAuthorize(Roles = new[] { "Admin", "Manager" })]
/// public class AdminHandler { ... }
///
/// // Require a named policy
/// [HandlerAuthorize(Policies = ["CanEditOrders"])]
/// public class EditOrderHandler { ... }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class HandlerAuthorizeAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the required roles for this handler.
    /// Multiple roles are treated as "any of" (the user must have at least one).
    /// </summary>
    public string[]? Roles { get; set; }

    /// <summary>
    /// Gets or sets the required authorization policies for this handler.
    /// All policies must be satisfied.
    /// </summary>
    public string[]? Policies { get; set; }
}
