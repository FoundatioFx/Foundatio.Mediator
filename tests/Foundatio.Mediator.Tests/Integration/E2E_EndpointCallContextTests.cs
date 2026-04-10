using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Foundatio.Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;

namespace Foundatio.Mediator.Tests.Integration;

/// <summary>
/// End-to-end tests verifying that HttpContext is resolved from CallContext
/// when a handler declares an HttpContext parameter in an endpoint scenario.
/// </summary>
public class E2E_EndpointCallContextTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    // ── Messages ───────────────────────────────────────────────────────────

    public record GetRequestPath();

    // ── Handler ────────────────────────────────────────────────────────────

    /// <summary>
    /// Handler that takes HttpContext as a parameter — resolved from CallContext
    /// which the endpoint generator populates automatically.
    /// </summary>
    public class RequestPathHandler
    {
        public string Handle(GetRequestPath query, HttpContext httpContext)
            => httpContext.Request.Path.Value ?? "(null)";
    }

    // ── Tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Endpoint_HandlerReceivesHttpContext_FromCallContext()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMediator(b => b.AddAssembly<RequestPathHandler>());

        await using var app = builder.Build();
        app.MapMediatorEndpoints();
        await app.StartAsync(TestCancellationToken);

        var client = app.GetTestClient();

        var response = await client.GetAsync(
            "/api/request-paths", TestCancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<string>(TestCancellationToken);
        Assert.Equal("/api/request-paths", result);
    }

    // ── POST with [FromHeader] on message property ─────────────────────────

    public record CreateTenantItem(
        string Name,
        [property: FromHeader(Name = "X-Tenant-Id")] string TenantId
    );

    public record TenantItemView(string Name, string TenantId);

    public class TenantItemHandler
    {
        public TenantItemView Handle(CreateTenantItem command)
            => new(command.Name, command.TenantId);
    }

    [Fact]
    public async Task Endpoint_PostWithFromHeader_MergesHeaderIntoMessage()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMediator(b => b.AddAssembly<TenantItemHandler>());

        await using var app = builder.Build();
        app.MapMediatorEndpoints();
        await app.StartAsync(TestCancellationToken);

        var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/tenant-items")
        {
            Content = JsonContent.Create(new { Name = "Widget" })
        };
        request.Headers.Add("X-Tenant-Id", "tenant-42");

        var response = await client.SendAsync(request, TestCancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<TenantItemView>(TestCancellationToken);
        Assert.NotNull(result);
        Assert.Equal("Widget", result.Name);
        Assert.Equal("tenant-42", result.TenantId);
    }

    // ── GET with [FromHeader] on message property (AsParameters path) ──────

    public record GetTenantInfo
    {
        [FromHeader(Name = "X-Tenant-Id")]
        public string TenantId { get; init; } = "";
    }

    public class TenantInfoHandler
    {
        public string Handle(GetTenantInfo query) => $"tenant:{query.TenantId}";
    }

    [Fact]
    public async Task Endpoint_GetWithFromHeader_AsParametersBindsHeader()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMediator(b => b.AddAssembly<TenantInfoHandler>());

        await using var app = builder.Build();
        app.MapMediatorEndpoints();
        await app.StartAsync(TestCancellationToken);

        var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tenant-infos");
        request.Headers.Add("X-Tenant-Id", "tenant-99");

        var response = await client.SendAsync(request, TestCancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<string>(TestCancellationToken);
        Assert.Equal("tenant:tenant-99", result);
    }

    // ── GET with ClaimsPrincipal handler parameter ─────────────────────────

    public record GetCurrentUser();

    public class CurrentUserHandler
    {
        public string Handle(GetCurrentUser query, ClaimsPrincipal user)
            => user.Identity?.Name ?? "anonymous";
    }

    [Fact]
    public async Task Endpoint_HandlerReceivesClaimsPrincipal_FromCallContext()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMediator(b => b.AddAssembly<CurrentUserHandler>());

        await using var app = builder.Build();

        // Add test middleware that sets a ClaimsPrincipal before the endpoint runs
        app.Use(async (context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "test-user")], "TestScheme"));
            await next();
        });

        app.MapMediatorEndpoints();
        await app.StartAsync(TestCancellationToken);

        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/current-users", TestCancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<string>(TestCancellationToken);
        Assert.Equal("test-user", result);
    }

    // ── Result.Accepted with Location header ──────────────────────────────

    public record CreateExportJob(string Name);

    public class ExportJobHandler
    {
        public Result<string> Handle(CreateExportJob command)
            => Result<string>.Accepted("Export queued", "/api/exports/status/job-123");
    }

    [Fact]
    public async Task Endpoint_ResultAcceptedWithLocation_Returns202WithLocationHeader()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMediator(b => b.AddAssembly<ExportJobHandler>());

        await using var app = builder.Build();
        app.MapMediatorEndpoints();
        await app.StartAsync(TestCancellationToken);

        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/export-jobs", new { Name = "Q1 Report" }, TestCancellationToken);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("/api/exports/status/job-123", response.Headers.Location?.ToString());
    }
}
