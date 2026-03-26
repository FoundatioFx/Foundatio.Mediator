using Foundatio.Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Net.Http.Json;

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
}
