using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator;

/// <summary>
/// Default <see cref="IHandlerAuthorizationService"/> that checks authentication status
/// and roles using <see cref="ClaimsPrincipal.IsInRole(string)"/>.
/// <para>
/// Named policies are not evaluated by this implementation — if policies are specified,
/// a warning is logged and authorization succeeds (assuming role/auth checks pass).
/// To enforce named policies, register a custom <see cref="IHandlerAuthorizationService"/>
/// that delegates to ASP.NET Core's <c>IAuthorizationService</c>.
/// </para>
/// </summary>
public sealed class DefaultHandlerAuthorizationService : IHandlerAuthorizationService
{
    private readonly ILogger<DefaultHandlerAuthorizationService> _logger;
    private bool _policyWarningLogged;

    /// <summary>
    /// Creates a new instance of <see cref="DefaultHandlerAuthorizationService"/>.
    /// </summary>
    public DefaultHandlerAuthorizationService(ILogger<DefaultHandlerAuthorizationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ValueTask<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal? principal,
        AuthorizationRequirements requirements,
        CancellationToken cancellationToken = default)
    {
        // AllowAnonymous overrides everything
        if (requirements.AllowAnonymous)
            return new ValueTask<AuthorizationResult>(AuthorizationResult.Success());

        // Check authentication
        if (principal?.Identity?.IsAuthenticated != true)
            return new ValueTask<AuthorizationResult>(AuthorizationResult.Unauthorized());

        // Check roles (any-of semantics)
        if (requirements.Roles.Length > 0)
        {
            bool hasRole = false;
            foreach (var role in requirements.Roles)
            {
                if (principal.IsInRole(role))
                {
                    hasRole = true;
                    break;
                }
            }

            if (!hasRole)
            {
                var rolesStr = string.Join(", ", requirements.Roles);
                return new ValueTask<AuthorizationResult>(
                    AuthorizationResult.Forbidden($"User does not have any of the required roles: {rolesStr}"));
            }
        }

        // Warn about policies (not enforced by default implementation)
        if (requirements.Policies.Length > 0 && !_policyWarningLogged)
        {
            _policyWarningLogged = true;
            _logger.LogWarning(
                "Authorization policies ({Policies}) are configured but the default " +
                "authorization service does not evaluate them. Register a custom " +
                "IHandlerAuthorizationService to enforce named policies.",
                string.Join(", ", requirements.Policies));
        }

        return new ValueTask<AuthorizationResult>(AuthorizationResult.Success());
    }
}
