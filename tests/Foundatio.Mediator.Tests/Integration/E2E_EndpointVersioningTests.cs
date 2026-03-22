using System.Net;
using System.Net.Http.Json;
using Foundatio.Mediator.Tests.Fixtures;
using Foundatio.Xunit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_EndpointVersioningTests(ITestOutputHelper output) : TestWithLoggingBase(output)
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

    // ── Version 1 (fallback) returns full model ────────────────────────

    [Fact]
    public async Task GetWidget_Version1_ReturnsFullModel()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("Api-Version", "1");
        var response = await client.GetAsync("/api/widgets/w-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var widget = await response.Content.ReadFromJsonAsync<TestWidgetFull>();
        Assert.NotNull(widget);
        Assert.Equal("w-1", widget.Id);
        Assert.Equal("Alpha", widget.Name);
        Assert.Equal("The alpha widget", widget.Description);
        Assert.Equal(10.00m, widget.Price);
    }

    [Fact]
    public async Task GetWidgets_Version1_ReturnsFullModels()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("Api-Version", "1");
        var response = await client.GetAsync("/api/widgets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var widgets = await response.Content.ReadFromJsonAsync<List<TestWidgetFull>>();
        Assert.NotNull(widgets);
        Assert.Equal(2, widgets.Count);
        // Full model has Description
        Assert.False(string.IsNullOrEmpty(widgets[0].Description));
    }

    // ── Version 2 returns simplified DTO ────────────────────────────────

    [Fact]
    public async Task GetWidget_Version2_ReturnsSimplifiedDto()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("Api-Version", "2");
        var response = await client.GetAsync("/api/widgets/w-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var widget = await response.Content.ReadFromJsonAsync<TestWidgetDto>();
        Assert.NotNull(widget);
        Assert.Equal("w-1", widget.Id);
        Assert.Equal("Alpha", widget.Name);
        Assert.Equal(10.00m, widget.Price);
    }

    [Fact]
    public async Task GetWidgets_Version2_ReturnsSimplifiedDtos()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("Api-Version", "2");
        var response = await client.GetAsync("/api/widgets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var widgets = await response.Content.ReadFromJsonAsync<List<TestWidgetDto>>();
        Assert.NotNull(widgets);
        Assert.Equal(2, widgets.Count);
    }

    // ── No version header defaults to latest (v2) ──────────────────────

    [Fact]
    public async Task GetWidget_NoVersionHeader_DefaultsToLatest()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        // No Api-Version header — should default to version "2" (latest declared)
        var response = await client.GetAsync("/api/widgets/w-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Default version is "2" so we get the simplified DTO
        var widget = await response.Content.ReadFromJsonAsync<TestWidgetDto>();
        Assert.NotNull(widget);
        Assert.Equal("w-1", widget.Id);
        Assert.Equal("Alpha", widget.Name);
    }

    // ── Not found still returns 404 regardless of version ──────────────

    [Fact]
    public async Task GetWidget_NotFound_Version1_Returns404()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("Api-Version", "1");
        var response = await client.GetAsync("/api/widgets/nonexistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetWidget_NotFound_Version2_Returns404()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("Api-Version", "2");
        var response = await client.GetAsync("/api/widgets/nonexistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Unversioned handler serves all versions ────────────────────────

    [Fact]
    public async Task UnversionedEndpoint_ServesVersion1()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("Api-Version", "1");
        var response = await client.GetAsync("/api/items/item-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = await response.Content.ReadFromJsonAsync<TestItem>();
        Assert.NotNull(item);
        Assert.Equal("item-1", item.Id);
    }

    [Fact]
    public async Task UnversionedEndpoint_ServesVersion2()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("Api-Version", "2");
        var response = await client.GetAsync("/api/items/item-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = await response.Content.ReadFromJsonAsync<TestItem>();
        Assert.NotNull(item);
        Assert.Equal("item-1", item.Id);
    }

    // ── Invalid version returns error with supported versions header ──

    [Fact]
    public async Task GetWidget_InvalidVersion_Returns400WithSupportedVersions()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        // "99" is not a declared version — should get 400 with supported versions header
        client.DefaultRequestHeaders.Add("Api-Version", "99");
        var response = await client.GetAsync("/api/widgets/w-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.Contains("Api-Version-Supported"));
        var supported = response.Headers.GetValues("Api-Version-Supported").First();
        Assert.Contains("1", supported);
        Assert.Contains("2", supported);
    }

    // ── No version header on multi-versioned route without fallback ────

    [Fact]
    public async Task GetGadget_NoVersionHeader_DefaultsToLatest()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        // Gadgets have v1 and v2 handlers only (no unversioned fallback).
        // No Api-Version header — MatcherPolicy defaults to latest ("2"),
        // which matches TestGadgetV2Handler.
        var response = await client.GetAsync("/api/gadgets/g-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var gadget = await response.Content.ReadFromJsonAsync<TestGadgetV2>();
        Assert.NotNull(gadget);
        Assert.Equal("g-1", gadget.Id);
        Assert.Equal("Gadget-V2", gadget.Name);
    }

    [Fact]
    public async Task GetGadget_Version1_ReturnsV1WithSku()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("Api-Version", "1");
        var response = await client.GetAsync("/api/gadgets/g-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var gadget = await response.Content.ReadFromJsonAsync<TestGadgetV1>();
        Assert.NotNull(gadget);
        Assert.Equal("g-1", gadget.Id);
        Assert.Equal("Gadget-V1", gadget.Name);
        Assert.Equal("SKU-001", gadget.Sku);
    }

    [Fact]
    public async Task GetGadget_Version2_ReturnsV2WithoutSku()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        client.DefaultRequestHeaders.Add("Api-Version", "2");
        var response = await client.GetAsync("/api/gadgets/g-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var gadget = await response.Content.ReadFromJsonAsync<TestGadgetV2>();
        Assert.NotNull(gadget);
        Assert.Equal("g-1", gadget.Id);
        Assert.Equal("Gadget-V2", gadget.Name);
    }

    [Fact]
    public async Task GetGadget_InvalidVersion_Returns400WithSupportedVersions()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        // "99" is not a declared version — should get 400 with supported versions header
        client.DefaultRequestHeaders.Add("Api-Version", "99");
        var response = await client.GetAsync("/api/gadgets/g-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.Contains("Api-Version-Supported"));
        var supported = response.Headers.GetValues("Api-Version-Supported").First();
        Assert.Contains("1", supported);
        Assert.Contains("2", supported);
    }

    // ── ApiVersionContext is available to handlers ──────────────────────

    [Fact]
    public async Task ApiVersionContext_IsRegistered()
    {
        var (app, client) = await StartApp();
        await using var _ = app;

        // Verify the version context service is registered
        using var scope = app.Services.CreateScope();
        var versionContext = scope.ServiceProvider.GetService<IApiVersionContext>();
        Assert.NotNull(versionContext);
    }
}
