using System.Net;
using System.Net.Http.Json;
using Foundatio.Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

/// <summary>
/// End-to-end tests for <see cref="HandlerResult.ContinueWith"/>: Before middleware can replace
/// the message, and the rest of the pipeline — subsequent middleware, the handler, and
/// After/Finally methods — dispatches the replacement.
/// </summary>
public class E2E_MessageReplacementTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    // ── Replacement flows to handler and After ─────────────────────────────

    public record StampMe(string Value);

    [Middleware(Lifetime = MediatorLifetime.Singleton)]
    public class StampingMiddleware
    {
        public List<string> SeenInAfter { get; } = new();

        public HandlerResult Before(StampMe message)
            => HandlerResult.ContinueWith(message with { Value = message.Value + "+stamped" });

        public void After(StampMe message) => SeenInAfter.Add(message.Value);
    }

    public class StampMeHandler
    {
        public string Handle(StampMe message) => message.Value;
    }

    [Fact]
    public async Task ContinueWith_ReplacementMessage_FlowsToHandlerAndAfter()
    {
        var services = new ServiceCollection();
        services.AddSingleton<StampingMiddleware>();
        services.AddMediator(b => b.AddAssembly<StampMeHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.InvokeAsync<string>(new StampMe("original"), TestCancellationToken);

        Assert.Equal("original+stamped", result);

        var mw = provider.GetRequiredService<StampingMiddleware>();
        Assert.Equal(["original+stamped"], mw.SeenInAfter);
    }

    // ── Chained replacements run in middleware order ───────────────────────

    public record ChainedMessage(string Trail);

    [Middleware(Order = 1)]
    public static class FirstReplacingMiddleware
    {
        public static HandlerResult Before(ChainedMessage message)
            => HandlerResult.ContinueWith(message with { Trail = message.Trail + ",first" });
    }

    [Middleware(Order = 2)]
    public static class SecondReplacingMiddleware
    {
        public static HandlerResult Before(ChainedMessage message)
            => HandlerResult.ContinueWith(message with { Trail = message.Trail + ",second" });
    }

    public class ChainedMessageHandler
    {
        public string Handle(ChainedMessage message) => message.Trail;
    }

    [Fact]
    public async Task ContinueWith_ChainedMiddleware_EachSeesPreviousReplacement()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<ChainedMessageHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.InvokeAsync<string>(new ChainedMessage("start"), TestCancellationToken);

        Assert.Equal("start,first,second", result);
    }

    // ── Continue (no replacement) leaves the message untouched ─────────────

    public record PassthroughMessage(string Value);

    [Middleware]
    public static class PassthroughMiddleware
    {
        public static HandlerResult Before(PassthroughMessage message) => HandlerResult.Continue();
    }

    public class PassthroughMessageHandler
    {
        public string Handle(PassthroughMessage message) => message.Value;
    }

    [Fact]
    public async Task Continue_WithoutReplacement_DispatchesOriginalMessage()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<PassthroughMessageHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.InvokeAsync<string>(new PassthroughMessage("unchanged"), TestCancellationToken);

        Assert.Equal("unchanged", result);
    }

    // ── Endpoint: middleware stamps tenant from HttpContext ────────────────

    public record CreateStampedItem(string Name, string TenantId);

    public record StampedItemView(string Name, string TenantId);

    [Middleware]
    public static class TenantStampingMiddleware
    {
        public static HandlerResult Before(CreateStampedItem message, HttpContext? httpContext)
        {
            if (httpContext is null)
                return HandlerResult.Continue();

            return HandlerResult.ContinueWith(message with { TenantId = httpContext.Request.Headers["X-Tenant-Id"].ToString() });
        }
    }

    public class StampedItemHandler
    {
        public StampedItemView Handle(CreateStampedItem command)
            => new(command.Name, command.TenantId);
    }

    [Fact]
    public async Task Endpoint_MiddlewareContinueWith_StampsBodyBoundMessageFromHeader()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddMediator(b => b.AddAssembly<StampedItemHandler>());

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
    public async Task InvokeAsync_TenantMiddlewareWithoutHttpContext_DispatchesOriginalMessage()
    {
        // The same middleware no-ops outside an HTTP dispatch: HttpContext? resolves to null.
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<StampedItemHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var result = await mediator.InvokeAsync<StampedItemView>(
            new CreateStampedItem("Widget", "direct-tenant"), TestCancellationToken);

        Assert.Equal("direct-tenant", result.TenantId);
    }
}
