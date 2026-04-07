using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Foundatio.Mediator.Tests.Fixtures;
using Foundatio.Xunit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_EndpointTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    private static async Task<(WebApplication App, HttpClient Client)> StartApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddMediator();
        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMediatorEndpoints();
        await app.StartAsync();
        var client = app.GetTestClient();
        return (app, client);
    }

    // ── GET with route parameter ────────────────────────────────────────

    [Fact]
    public async Task GetItem_ReturnsItem()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        var response = await client.GetAsync("/api/items/item-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = await response.Content.ReadFromJsonAsync<TestItem>();
        Assert.NotNull(item);
        Assert.Equal("item-1", item.Id);
        Assert.Equal("Widget", item.Name);
        Assert.Equal(9.99m, item.Price);
    }

    [Fact]
    public async Task GetItem_NotFound_Returns404()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        var response = await client.GetAsync("/api/items/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── GET collection ──────────────────────────────────────────────────

    [Fact]
    public async Task GetItems_ReturnsList()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        var response = await client.GetAsync("/api/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<TestItem>>();
        Assert.NotNull(items);
        Assert.Equal(2, items.Count);
    }

    // ── POST with JSON body ─────────────────────────────────────────────

    [Fact]
    public async Task CreateItem_WithAuth_ReturnsItem()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        var response = await client.PostAsJsonAsync("/api/items", new { Name = "New", Price = 5.0m });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = await response.Content.ReadFromJsonAsync<TestItem>();
        Assert.NotNull(item);
        Assert.Equal("New", item.Name);
    }

    [Fact]
    public async Task CreateItem_WithoutAuth_Returns401()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        var response = await client.PostAsJsonAsync("/api/items", new { Name = "New", Price = 5.0m });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateItem_WrongRole_Returns403()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("X-Test-Role", "User");
        var response = await client.PostAsJsonAsync("/api/items", new { Name = "New", Price = 5.0m });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── DELETE with route parameter ─────────────────────────────────────

    [Fact]
    public async Task DeleteItem_WithAuth_Returns204()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        var response = await client.DeleteAsync("/api/items/item-1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteItem_NotFound_Returns404()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        var response = await client.DeleteAsync("/api/items/nonexistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

// ── Test auth handler ───────────────────────────────────────────────────

internal sealed class TestAuthHandler(
    Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
    Microsoft.Extensions.Logging.ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check for the test role header to simulate authenticated user
        var role = Request.Headers["X-Test-Role"].FirstOrDefault();
        if (string.IsNullOrEmpty(role))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "TestUser"),
            new(ClaimTypes.NameIdentifier, "test-user-1"),
            new(ClaimTypes.Role, role),
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
