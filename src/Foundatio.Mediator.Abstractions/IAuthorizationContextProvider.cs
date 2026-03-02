using System.Security.Claims;

namespace Foundatio.Mediator;

/// <summary>
/// Provides the current user's <see cref="ClaimsPrincipal"/> for handler authorization checks.
/// <para>
/// In ASP.NET Core apps, a generated implementation reads from <c>IHttpContextAccessor.HttpContext.User</c>.
/// In console apps or background workers, the default implementation reads from <c>Thread.CurrentPrincipal</c>.
/// </para>
/// <para>
/// Users can register their own implementation to customize how the current principal is resolved.
/// </para>
/// </summary>
public interface IAuthorizationContextProvider
{
    /// <summary>
    /// Gets the current user's <see cref="ClaimsPrincipal"/>.
    /// Returns <c>null</c> if no user is authenticated.
    /// </summary>
    ClaimsPrincipal? GetCurrentPrincipal();
}
