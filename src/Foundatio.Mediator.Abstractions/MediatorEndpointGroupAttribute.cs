namespace Foundatio.Mediator;

/// <summary>
/// Defines a reusable, named endpoint group at the assembly level. Handlers join the group by
/// referencing its <see cref="Name"/> via <c>[HandlerEndpoint(Group = "...")]</c>, and inherit the
/// group's route prefix, OpenAPI tags, endpoint filters, authorization policies, and description
/// visibility — without each handler class needing its own <see cref="HandlerEndpointGroupAttribute"/>.
/// </summary>
/// <remarks>
/// Apply one attribute per named group. Group definitions are read from the assembly being compiled.
/// <example>
/// <code>
/// [assembly: MediatorEndpointGroup(
///     Name = "Admin",
///     RoutePrefix = "/api/v2/admin",
///     Policies = [AuthorizationRoles.GlobalAdminPolicy],
///     EndpointFilters = [typeof(AutoValidationEndpointFilter)],
///     ExcludeFromDescription = true)]
///
/// public class SettingsHandler
/// {
///     [HandlerEndpoint(HandlerMethod.Get, "settings", Group = "Admin")]
///     public Result&lt;Settings&gt; Handle(GetSettings query) => ...;
/// }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, Inherited = false, AllowMultiple = true)]
public sealed class MediatorEndpointGroupAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MediatorEndpointGroupAttribute"/> class.
    /// </summary>
    public MediatorEndpointGroupAttribute() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="MediatorEndpointGroupAttribute"/> class.
    /// </summary>
    /// <param name="name">The group name referenced by <c>[HandlerEndpoint(Group = "...")]</c>.</param>
    public MediatorEndpointGroupAttribute(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Gets or sets the group name. Handlers reference this value via
    /// <c>[HandlerEndpoint(Group = "...")]</c>. Also used as the default OpenAPI tag.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the route prefix shared by all endpoints in this group, relative to the global
    /// <c>EndpointRoutePrefix</c>. Use a leading <c>/</c> for an absolute prefix that bypasses the
    /// global prefix. When null, the prefix is derived from <see cref="Name"/> (kebab-cased).
    /// </summary>
    public string? RoutePrefix { get; set; }

    /// <summary>
    /// Gets or sets the OpenAPI tags for endpoints in this group. When null, <see cref="Name"/> is
    /// used as a single tag.
    /// </summary>
    public string[]? Tags { get; set; }

    /// <summary>
    /// Gets or sets the endpoint filter types applied to every endpoint in this group.
    /// Each type must implement <c>Microsoft.AspNetCore.Http.IEndpointFilter</c>.
    /// </summary>
    public Type[]? EndpointFilters { get; set; }

    /// <summary>
    /// Gets or sets the authorization policy names required by every endpoint in this group.
    /// Emitted as <c>.RequireAuthorization("policy")</c> on the group.
    /// </summary>
    public string[]? Policies { get; set; }

    /// <summary>
    /// Gets or sets whether endpoints in this group are hidden from the OpenAPI description
    /// (via <c>.ExcludeFromDescription()</c> on the group) while remaining routable.
    /// </summary>
    public bool ExcludeFromDescription { get; set; }
}
