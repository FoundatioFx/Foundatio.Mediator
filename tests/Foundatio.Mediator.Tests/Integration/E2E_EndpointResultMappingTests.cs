using System.Net;
using System.Net.Http.Json;
using Foundatio.Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

/// <summary>
/// End-to-end tests for the generated default result-to-HTTP mapping.
/// </summary>
public class E2E_EndpointResultMappingTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    public record GetThrottledResource();

    public class ThrottledResourceHandler
    {
        public Result<string> Handle(GetThrottledResource query)
            => Result<string>.RateLimited("Slow down");
    }

    [Fact]
    public async Task Endpoint_ResultRateLimited_Returns429Problem()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMediator(b => b.AddAssembly<ThrottledResourceHandler>());

        await using var app = builder.Build();
        app.MapMediatorEndpoints();
        await app.StartAsync(TestCancellationToken);

        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/throttled-resources", TestCancellationToken);

        Assert.Equal((HttpStatusCode)429, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(TestCancellationToken);
        Assert.NotNull(problem);
        Assert.Equal(429, problem.Status);
        Assert.Equal("Slow down", problem.Detail);
    }
}
