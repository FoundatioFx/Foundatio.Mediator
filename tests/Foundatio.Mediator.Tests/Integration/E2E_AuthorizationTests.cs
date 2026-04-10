using System.Security.Claims;
using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

/// <summary>
/// End-to-end tests verifying authorization enforcement through the mediator pipeline.
/// Covers both Result&lt;T&gt; returning handlers (which return Result.Forbidden/Unauthorized)
/// and non-Result handlers (which throw UnauthorizedAccessException).
/// </summary>
public class E2E_AuthorizationTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    // ── Messages ───────────────────────────────────────────────────────────

    public record AuthGetOrder(string Id);
    public record AuthOrderView(string Id);

    public record AuthDeleteOrder(string Id);

    public record AuthGetPublicInfo();

    public record AuthAdminAction(string Data);
    public record AuthAdminResult(string Result);

    public record AuthFireAndForget(string Data);

    // ── Handlers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Handler with [HandlerAuthorize] returning Result&lt;T&gt;.
    /// Auth failures should produce Result.Unauthorized/Result.Forbidden.
    /// </summary>
    [HandlerAuthorize]
    public class AuthResultHandler
    {
        public Result<AuthOrderView> Handle(AuthGetOrder query)
            => new AuthOrderView(query.Id);
    }

    /// <summary>
    /// Handler with [HandlerAuthorize] returning void (non-Result).
    /// Auth failures should throw UnauthorizedAccessException.
    /// </summary>
    [HandlerAuthorize]
    public class AuthVoidHandler
    {
        public static bool WasExecuted { get; set; }

        public void Handle(AuthDeleteOrder command)
        {
            WasExecuted = true;
        }
    }

    /// <summary>
    /// Handler with [HandlerAllowAnonymous] — should always succeed regardless of auth state.
    /// </summary>
    [HandlerAllowAnonymous]
    public class AuthPublicHandler
    {
        public string Handle(AuthGetPublicInfo query) => "public data";
    }

    /// <summary>
    /// Handler with role-based [HandlerAuthorize] returning Result&lt;T&gt;.
    /// </summary>
    public class AuthRoleHandler
    {
        [HandlerAuthorize(Roles = ["Admin", "Manager"])]
        public Result<AuthAdminResult> Handle(AuthAdminAction cmd) => new AuthAdminResult("done");
    }

    /// <summary>
    /// Handler with role-based [HandlerAuthorize] returning void (non-Result).
    /// </summary>
    public class AuthRoleVoidHandler
    {
        public static bool WasExecuted { get; set; }

        [HandlerAuthorize(Roles = ["Admin"])]
        public void Handle(AuthFireAndForget cmd)
        {
            WasExecuted = true;
        }
    }

    // ── Test auth context provider ─────────────────────────────────────────

    private sealed class TestAuthContextProvider : IAuthorizationContextProvider
    {
        public ClaimsPrincipal? Principal { get; set; }
        public ClaimsPrincipal? GetCurrentPrincipal() => Principal;
    }

    private static ClaimsPrincipal CreatePrincipal(params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "testuser") };
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private (ServiceProvider Provider, IMediator Mediator, TestAuthContextProvider AuthContext) CreateServices()
    {
        var authContext = new TestAuthContextProvider();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAuthorizationContextProvider>(authContext);
        services.AddMediator(b => b.AddAssembly<AuthResultHandler>());
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();
        return (provider, mediator, authContext);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Result<T> handler tests
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResultHandler_NoUser_ReturnsUnauthorized()
    {
        var (provider, mediator, authContext) = CreateServices();
        await using var _ = provider;
        authContext.Principal = null; // no user

        var result = await mediator.InvokeAsync<Result<AuthOrderView>>(new AuthGetOrder("1"), TestCancellationToken);

        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task ResultHandler_UnauthenticatedPrincipal_ReturnsUnauthorized()
    {
        var (provider, mediator, authContext) = CreateServices();
        await using var _ = provider;
        authContext.Principal = new ClaimsPrincipal(new ClaimsIdentity()); // not authenticated

        var result = await mediator.InvokeAsync<Result<AuthOrderView>>(new AuthGetOrder("1"), TestCancellationToken);

        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task ResultHandler_AuthenticatedUser_ReturnsSuccess()
    {
        var (provider, mediator, authContext) = CreateServices();
        await using var _ = provider;
        authContext.Principal = CreatePrincipal();

        var result = await mediator.InvokeAsync<Result<AuthOrderView>>(new AuthGetOrder("42"), TestCancellationToken);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal("42", result.Value!.Id);
    }

    [Fact]
    public async Task ResultHandler_WithRoles_CorrectRole_ReturnsSuccess()
    {
        var (provider, mediator, authContext) = CreateServices();
        await using var _ = provider;
        authContext.Principal = CreatePrincipal("Admin");

        var result = await mediator.InvokeAsync<Result<AuthAdminResult>>(new AuthAdminAction("test"), TestCancellationToken);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Equal("done", result.Value!.Result);
    }

    [Fact]
    public async Task ResultHandler_WithRoles_AnyRoleMatches_ReturnsSuccess()
    {
        var (provider, mediator, authContext) = CreateServices();
        await using var _ = provider;
        authContext.Principal = CreatePrincipal("Manager"); // Manager is one of the allowed roles

        var result = await mediator.InvokeAsync<Result<AuthAdminResult>>(new AuthAdminAction("test"), TestCancellationToken);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ResultHandler_WithRoles_WrongRole_ReturnsForbidden()
    {
        var (provider, mediator, authContext) = CreateServices();
        await using var _ = provider;
        authContext.Principal = CreatePrincipal("Viewer");

        var result = await mediator.InvokeAsync<Result<AuthAdminResult>>(new AuthAdminAction("test"), TestCancellationToken);

        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task ResultHandler_WithRoles_NoUser_ReturnsUnauthorized()
    {
        var (provider, mediator, authContext) = CreateServices();
        await using var _ = provider;
        authContext.Principal = null;

        var result = await mediator.InvokeAsync<Result<AuthAdminResult>>(new AuthAdminAction("test"), TestCancellationToken);

        Assert.NotNull(result);
        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Non-Result (void) handler tests
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task VoidHandler_NoUser_ThrowsUnauthorizedAccessException()
    {
        AuthVoidHandler.WasExecuted = false;
        var (provider, mediator, authContext) = CreateServices();
        await using var _ = provider;
        authContext.Principal = null;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => mediator.InvokeAsync(new AuthDeleteOrder("1"), TestCancellationToken).AsTask());

        Assert.False(AuthVoidHandler.WasExecuted);
    }

    [Fact]
    public async Task VoidHandler_UnauthenticatedPrincipal_ThrowsUnauthorizedAccessException()
    {
        AuthVoidHandler.WasExecuted = false;
        var (provider, mediator, authContext) = CreateServices();
        await using var _ = provider;
        authContext.Principal = new ClaimsPrincipal(new ClaimsIdentity());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => mediator.InvokeAsync(new AuthDeleteOrder("1"), TestCancellationToken).AsTask());

        Assert.False(AuthVoidHandler.WasExecuted);
    }

    [Fact]
    public async Task VoidHandler_AuthenticatedUser_Succeeds()
    {
        AuthVoidHandler.WasExecuted = false;
        var (provider, mediator, authContext) = CreateServices();
        await using var _ = provider;
        authContext.Principal = CreatePrincipal();

        await mediator.InvokeAsync(new AuthDeleteOrder("1"), TestCancellationToken);

        Assert.True(AuthVoidHandler.WasExecuted);
    }

    [Fact]
    public async Task VoidHandler_WithRoles_WrongRole_ThrowsUnauthorizedAccessException()
    {
        AuthRoleVoidHandler.WasExecuted = false;
        var (provider, mediator, authContext) = CreateServices();
        await using var _ = provider;
        authContext.Principal = CreatePrincipal("Viewer");

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => mediator.InvokeAsync(new AuthFireAndForget("test"), TestCancellationToken).AsTask());

        Assert.False(AuthRoleVoidHandler.WasExecuted);
        Assert.Contains("role", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VoidHandler_WithRoles_CorrectRole_Succeeds()
    {
        AuthRoleVoidHandler.WasExecuted = false;
        var (provider, mediator, authContext) = CreateServices();
        await using var _ = provider;
        authContext.Principal = CreatePrincipal("Admin");

        await mediator.InvokeAsync(new AuthFireAndForget("test"), TestCancellationToken);

        Assert.True(AuthRoleVoidHandler.WasExecuted);
    }

    // ══════════════════════════════════════════════════════════════════════
    // [HandlerAllowAnonymous] tests
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AllowAnonymous_NoUser_Succeeds()
    {
        var (provider, mediator, authContext) = CreateServices();
        await using var _ = provider;
        authContext.Principal = null;

        var result = await mediator.InvokeAsync<string>(new AuthGetPublicInfo(), TestCancellationToken);

        Assert.Equal("public data", result);
    }

    [Fact]
    public async Task AllowAnonymous_AuthenticatedUser_Succeeds()
    {
        var (provider, mediator, authContext) = CreateServices();
        await using var _ = provider;
        authContext.Principal = CreatePrincipal("Admin");

        var result = await mediator.InvokeAsync<string>(new AuthGetPublicInfo(), TestCancellationToken);

        Assert.Equal("public data", result);
    }
}
