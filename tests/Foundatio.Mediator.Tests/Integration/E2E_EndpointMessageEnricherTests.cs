using System.Net;
using System.Net.Http.Json;
using Foundatio.Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

/// <summary>
/// End-to-end tests for <see cref="IMediatorMessageEnricher{TContext}"/>: registered enrichers
/// run against the bound message before the handler is invoked, in registration order.
/// </summary>
public class E2E_EndpointMessageEnricherTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    // ── POST body-bound message enriched from a request header ─────────────

    public record CreateStampedItem(string Name, string TenantId);

    public record StampedItemView(string Name, string TenantId);

    public class StampedItemHandler
    {
        public StampedItemView Handle(CreateStampedItem command)
            => new(command.Name, command.TenantId);
    }

    public class TenantStampingEnricher : IMediatorMessageEnricher<HttpContext>
    {
        public ValueTask<object> EnrichAsync(object message, HttpContext context, CancellationToken cancellationToken)
        {
            if (message is CreateStampedItem item)
                return ValueTask.FromResult<object>(item with { TenantId = context.Request.Headers["X-Tenant-Id"].ToString() });

            return ValueTask.FromResult(message);
        }
    }

    [Fact]
    public async Task Endpoint_WithEnricher_StampsBodyBoundMessageFromHeader()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMediator(b => b.AddAssembly<StampedItemHandler>());
        builder.Services.AddSingleton<IMediatorMessageEnricher<HttpContext>, TenantStampingEnricher>();

        await using var app = builder.Build();
        app.MapMediatorEndpoints();
        await app.StartAsync(TestCancellationToken);

        var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/stamped-items")
        {
            Content = JsonContent.Create(new { Name = "Widget", TenantId = "spoofed" })
        };
        request.Headers.Add("X-Tenant-Id", "tenant-42");

        var response = await client.SendAsync(request, TestCancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<StampedItemView>(TestCancellationToken);
        Assert.NotNull(result);
        Assert.Equal("Widget", result.Name);
        Assert.Equal("tenant-42", result.TenantId);
    }

    [Fact]
    public async Task Endpoint_WithoutEnricher_DispatchesBoundMessageUnchanged()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMediator(b => b.AddAssembly<StampedItemHandler>());

        await using var app = builder.Build();
        app.MapMediatorEndpoints();
        await app.StartAsync(TestCancellationToken);

        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/stamped-items", new { Name = "Widget", TenantId = "from-body" }, TestCancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<StampedItemView>(TestCancellationToken);
        Assert.NotNull(result);
        Assert.Equal("from-body", result.TenantId);
    }

    // ── GET constructed message enriched from route/header context ─────────

    public record GetStampedInfo(string Scope);

    public class StampedInfoHandler
    {
        public string Handle(GetStampedInfo query) => $"scope:{query.Scope}";
    }

    public class ScopeStampingEnricher : IMediatorMessageEnricher<HttpContext>
    {
        public ValueTask<object> EnrichAsync(object message, HttpContext context, CancellationToken cancellationToken)
        {
            if (message is GetStampedInfo info)
                return ValueTask.FromResult<object>(info with { Scope = $"{info.Scope}+{context.Request.Headers["X-Scope"]}" });

            return ValueTask.FromResult(message);
        }
    }

    [Fact]
    public async Task Endpoint_WithEnricher_EnrichesQueryConstructedMessage()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMediator(b => b.AddAssembly<StampedInfoHandler>());
        builder.Services.AddSingleton<IMediatorMessageEnricher<HttpContext>, ScopeStampingEnricher>();

        await using var app = builder.Build();
        app.MapMediatorEndpoints();
        await app.StartAsync(TestCancellationToken);

        var client = app.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/stamped-infos?scope=base");
        request.Headers.Add("X-Scope", "extra");

        var response = await client.SendAsync(request, TestCancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<string>(TestCancellationToken);
        Assert.Equal("scope:base+extra", result);
    }

    // ── Multiple enrichers run in registration order ───────────────────────

    public record CreateOrderedItem(string Trail);

    public class OrderedItemHandler
    {
        public string Handle(CreateOrderedItem command) => command.Trail;
    }

    public class FirstEnricher : IMediatorMessageEnricher<HttpContext>
    {
        public ValueTask<object> EnrichAsync(object message, HttpContext context, CancellationToken cancellationToken)
        {
            if (message is CreateOrderedItem item)
                return ValueTask.FromResult<object>(item with { Trail = item.Trail + ",first" });

            return ValueTask.FromResult(message);
        }
    }

    public class SecondEnricher : IMediatorMessageEnricher<HttpContext>
    {
        public ValueTask<object> EnrichAsync(object message, HttpContext context, CancellationToken cancellationToken)
        {
            if (message is CreateOrderedItem item)
                return ValueTask.FromResult<object>(item with { Trail = item.Trail + ",second" });

            return ValueTask.FromResult(message);
        }
    }

    [Fact]
    public async Task Endpoint_WithMultipleEnrichers_RunsInRegistrationOrder()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMediator(b => b.AddAssembly<OrderedItemHandler>());
        builder.Services.AddSingleton<IMediatorMessageEnricher<HttpContext>, FirstEnricher>();
        builder.Services.AddSingleton<IMediatorMessageEnricher<HttpContext>, SecondEnricher>();

        await using var app = builder.Build();
        app.MapMediatorEndpoints();
        await app.StartAsync(TestCancellationToken);

        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/ordered-items", new { Trail = "start" }, TestCancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<string>(TestCancellationToken);
        Assert.Equal("start,first,second", result);
    }

    // ── Result.RateLimited maps to HTTP 429 ────────────────────────────────

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
