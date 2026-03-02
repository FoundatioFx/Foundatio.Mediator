using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundatio.Mediator.Tests;

/// <summary>
/// Unit tests for <see cref="DefaultHandlerAuthorizationService"/>,
/// <see cref="AuthorizationResult"/>, and <see cref="AuthorizationRequirements"/>.
/// </summary>
public class AuthorizationServiceTests
{
    private readonly DefaultHandlerAuthorizationService _service =
        new(NullLogger<DefaultHandlerAuthorizationService>.Instance);

    // ── AuthorizationRequirements ──────────────────────────────────────────

    [Fact]
    public void AuthorizationRequirements_Default_HasExpectedValues()
    {
        var req = AuthorizationRequirements.Default;
        Assert.False(req.Required);
        Assert.False(req.AllowAnonymous);
        Assert.Empty(req.Roles);
        Assert.Empty(req.Policies);
        Assert.False(req.ShouldEnforce);
    }

    [Fact]
    public void AuthorizationRequirements_ShouldEnforce_TrueWhenRequiredAndNotAnonymous()
    {
        var req = new AuthorizationRequirements(true, [], [], false);
        Assert.True(req.ShouldEnforce);
    }

    [Fact]
    public void AuthorizationRequirements_ShouldEnforce_FalseWhenAllowAnonymous()
    {
        var req = new AuthorizationRequirements(true, [], [], true);
        Assert.False(req.ShouldEnforce);
    }

    [Fact]
    public void AuthorizationRequirements_ShouldEnforce_FalseWhenNotRequired()
    {
        var req = new AuthorizationRequirements(false, [], [], false);
        Assert.False(req.ShouldEnforce);
    }

    [Fact]
    public void AuthorizationRequirements_NullArrays_DefaultToEmpty()
    {
        var req = new AuthorizationRequirements(true, null!, null!, false);
        Assert.NotNull(req.Roles);
        Assert.NotNull(req.Policies);
        Assert.Empty(req.Roles);
        Assert.Empty(req.Policies);
    }

    // ── AuthorizationResult ────────────────────────────────────────────────

    [Fact]
    public void AuthorizationResult_Success_HasExpectedProperties()
    {
        var result = AuthorizationResult.Success();
        Assert.True(result.Succeeded);
        Assert.Null(result.FailureReason);
        Assert.False(result.IsForbidden);
    }

    [Fact]
    public void AuthorizationResult_Unauthorized_HasExpectedProperties()
    {
        var result = AuthorizationResult.Unauthorized("Not logged in");
        Assert.False(result.Succeeded);
        Assert.False(result.IsForbidden);
        Assert.Equal("Not logged in", result.FailureReason);
    }

    [Fact]
    public void AuthorizationResult_Unauthorized_DefaultReason()
    {
        var result = AuthorizationResult.Unauthorized();
        Assert.Equal("Authentication is required.", result.FailureReason);
    }

    [Fact]
    public void AuthorizationResult_Forbidden_HasExpectedProperties()
    {
        var result = AuthorizationResult.Forbidden("Missing role");
        Assert.False(result.Succeeded);
        Assert.True(result.IsForbidden);
        Assert.Equal("Missing role", result.FailureReason);
    }

    [Fact]
    public void AuthorizationResult_Forbidden_DefaultReason()
    {
        var result = AuthorizationResult.Forbidden();
        Assert.Equal("Access is denied.", result.FailureReason);
    }

    // ── DefaultHandlerAuthorizationService ─────────────────────────────────

    [Fact]
    public async Task AuthorizeAsync_AllowAnonymous_ReturnsSuccess()
    {
        var req = new AuthorizationRequirements(true, ["Admin"], [], allowAnonymous: true);

        var result = await _service.AuthorizeAsync(null, req, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_NullPrincipal_ReturnsUnauthorized()
    {
        var req = new AuthorizationRequirements(true, [], [], false);

        var result = await _service.AuthorizeAsync(null, req, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.False(result.IsForbidden);
    }

    [Fact]
    public async Task AuthorizeAsync_UnauthenticatedPrincipal_ReturnsUnauthorized()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity()); // no auth type = not authenticated
        var req = new AuthorizationRequirements(true, [], [], false);

        var result = await _service.AuthorizeAsync(principal, req, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.False(result.IsForbidden);
    }

    [Fact]
    public async Task AuthorizeAsync_AuthenticatedNoRoleRequirement_ReturnsSuccess()
    {
        var principal = CreateAuthenticatedPrincipal();
        var req = new AuthorizationRequirements(true, [], [], false);

        var result = await _service.AuthorizeAsync(principal, req, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_HasRequiredRole_ReturnsSuccess()
    {
        var principal = CreateAuthenticatedPrincipal("Admin");
        var req = new AuthorizationRequirements(true, ["Admin"], [], false);

        var result = await _service.AuthorizeAsync(principal, req, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_MissingRequiredRole_ReturnsForbidden()
    {
        var principal = CreateAuthenticatedPrincipal("User");
        var req = new AuthorizationRequirements(true, ["Admin"], [], false);

        var result = await _service.AuthorizeAsync(principal, req, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.True(result.IsForbidden);
        Assert.Contains("Admin", result.FailureReason);
    }

    [Fact]
    public async Task AuthorizeAsync_MultipleRoles_AnyOf_ReturnsSuccess()
    {
        var principal = CreateAuthenticatedPrincipal("Manager");
        var req = new AuthorizationRequirements(true, ["Admin", "Manager"], [], false);

        var result = await _service.AuthorizeAsync(principal, req, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task AuthorizeAsync_MultipleRoles_NoneMatch_ReturnsForbidden()
    {
        var principal = CreateAuthenticatedPrincipal("Viewer");
        var req = new AuthorizationRequirements(true, ["Admin", "Manager"], [], false);

        var result = await _service.AuthorizeAsync(principal, req, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.True(result.IsForbidden);
    }

    [Fact]
    public async Task AuthorizeAsync_WithPolicies_LogsWarningAndSucceeds()
    {
        var loggerFactory = new TestLoggerFactory();
        var service = new DefaultHandlerAuthorizationService(loggerFactory.CreateLogger<DefaultHandlerAuthorizationService>());

        var principal = CreateAuthenticatedPrincipal();
        var req = new AuthorizationRequirements(true, [], ["CanEditOrders"], false);

        var result = await service.AuthorizeAsync(principal, req, TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.Contains(loggerFactory.Messages, m => m.Contains("CanEditOrders"));
    }

    [Fact]
    public async Task AuthorizeAsync_WithPolicies_LogsWarningOnlyOnce()
    {
        var loggerFactory = new TestLoggerFactory();
        var service = new DefaultHandlerAuthorizationService(loggerFactory.CreateLogger<DefaultHandlerAuthorizationService>());

        var principal = CreateAuthenticatedPrincipal();
        var req = new AuthorizationRequirements(true, [], ["Policy1"], false);

        await service.AuthorizeAsync(principal, req, TestContext.Current.CancellationToken);
        await service.AuthorizeAsync(principal, req, TestContext.Current.CancellationToken);
        await service.AuthorizeAsync(principal, req, TestContext.Current.CancellationToken);

        var policyWarnings = loggerFactory.Messages.Count(m => m.Contains("Policy1"));
        Assert.Equal(1, policyWarnings);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static ClaimsPrincipal CreateAuthenticatedPrincipal(params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "testuser"),
        };
        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Simple logger factory that captures messages for test assertions.
    /// </summary>
    private sealed class TestLoggerFactory : ILoggerFactory
    {
        private readonly List<string> _messages = new();
        public IReadOnlyList<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => new TestLogger(_messages);
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }

        private sealed class TestLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                messages.Add(formatter(state, exception));
            }
        }
    }
}
