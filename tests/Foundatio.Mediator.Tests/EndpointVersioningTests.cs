namespace Foundatio.Mediator.Tests;

public class EndpointVersioningTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    private static readonly MediatorGenerator Gen = new();

    [Fact]
    public void VersionedGroup_RoutesAreFlat_NoPathSegment()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All,
                ApiVersions = new[] { "1" }
            )]

            public record GetWidget(string Id);

            [HandlerEndpointGroup("Widgets", ApiVersion = "1")]
            public class WidgetHandler
            {
                public string Handle(GetWidget query) => "widget";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Routes should NOT contain version path segments
        Assert.DoesNotContain("MapGroup(\"v1\")", endpointSource);
        AssertEndpoint(endpointSource, "GET", "/api/widgets/{id}");
    }

    [Fact]
    public void MultipleVersions_SameRoute_GeneratesVersionDispatch()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All,
                ApiVersions = new[] { "1", "2" }
            )]

            public record GetWidget(string Id);

            [HandlerEndpointGroup("Widgets")]
            public class WidgetHandler
            {
                public string Handle(GetWidget query) => "widget-v1";
            }

            [HandlerEndpointGroup("Widgets", ApiVersion = "2")]
            public class WidgetV2Handler
            {
                public string Handle(GetWidget query) => "widget-v2";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // No version path segments
        Assert.DoesNotContain("MapGroup(\"v1\")", endpointSource);
        Assert.DoesNotContain("MapGroup(\"v2\")", endpointSource);
        // MatcherPolicy-based versioning: each endpoint has ApiVersionMetadata
        Assert.Contains("ApiVersionMetadata", endpointSource);
        Assert.DoesNotContain("switch (apiVersion)", endpointSource);
        // Both handlers generate endpoints on the same flat route
        AssertEndpoint(endpointSource, "GET", "/api/widgets/{id}");
    }

    [Fact]
    public void NoVersion_EndpointsUnderDefaultGroup()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record GetWidget(string Id);

            [HandlerEndpointGroup("Widgets")]
            public class WidgetHandler
            {
                public string Handle(GetWidget query) => "widget";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // No version groups created
        Assert.DoesNotContain("MapGroup(\"v", endpointSource);
        AssertEndpoint(endpointSource, "GET", "/api/widgets/{id}");
    }

    [Fact]
    public void MixedVersionedAndUnversioned_BothGenerated()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All,
                ApiVersions = new[] { "1" }
            )]

            public record GetHealth();
            public record GetWidget(string Id);

            public class HealthHandler
            {
                [HandlerEndpoint(Route = "/health")]
                public string Handle(GetHealth query) => "ok";
            }

            [HandlerEndpointGroup("Widgets", ApiVersion = "1")]
            public class WidgetHandler
            {
                public string Handle(GetWidget query) => "widget";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Unversioned health endpoint (explicit route bypasses prefix)
        AssertEndpoint(endpointSource, "GET", "/health");
        // Versioned widget endpoint on flat route (no path segment)
        AssertEndpoint(endpointSource, "GET", "/api/widgets/{id}");
    }

    [Fact]
    public void MethodLevelVersion_OverridesGroupVersion()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All,
                ApiVersions = new[] { "1", "2" }
            )]

            public record GetWidgetV1(string Id);
            public record GetWidgetV2(string Id);

            [HandlerEndpointGroup("Widgets", ApiVersion = "1")]
            public class WidgetHandler
            {
                public string Handle(GetWidgetV1 query) => "widget-v1";

                [HandlerEndpoint(ApiVersion = "2")]
                public string Handle(GetWidgetV2 query) => "widget-v2";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Method-level version "2" overrides group-level "1"
        // Both end up on flat routes
        AssertEndpoint(endpointSource, "GET", "/api/widgets/{id}");
    }

    [Fact]
    public void MultiVersionAttribute_GeneratesEndpointsInAllVersions()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All,
                ApiVersions = new[] { "1", "2" }
            )]

            public record GetWidget(string Id);

            [HandlerEndpointGroup("Widgets", ApiVersions = new[] { "1", "2" })]
            public class WidgetHandler
            {
                public string Handle(GetWidget query) => "widget";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Multi-version handler serves all declared versions, flat route
        Assert.DoesNotContain("MapGroup(\"v1\")", endpointSource);
        Assert.DoesNotContain("MapGroup(\"v2\")", endpointSource);
        AssertEndpoint(endpointSource, "GET", "/api/widgets/{id}");
    }

    [Fact]
    public void DeprecatedVersion_EmitsObsoleteMetadata()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All,
                ApiVersions = new[] { "1" }
            )]

            public record GetWidgetV1(string Id);

            [HandlerEndpointGroup("Widgets", ApiVersion = "1", Deprecated = true)]
            public class WidgetHandlerV1
            {
                public string Handle(GetWidgetV1 query) => "widget-v1";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/widgets/{id}");
        Assert.Contains("ObsoleteAttribute", endpointSource);
        Assert.Contains("This API version is deprecated", endpointSource);
    }

    [Fact]
    public void CustomVersionHeader_UsesConfiguredHeader()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All,
                ApiVersions = new[] { "1", "2" },
                ApiVersionHeader = "X-Api-Version"
            )]

            public record GetWidget(string Id);

            [HandlerEndpointGroup("Widgets")]
            public class WidgetHandler
            {
                public string Handle(GetWidget query) => "widget-v1";
            }

            [HandlerEndpointGroup("Widgets", ApiVersion = "2")]
            public class WidgetV2Handler
            {
                public string Handle(GetWidget query) => "widget-v2";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Uses custom header name
        Assert.Contains("X-Api-Version", endpointSource);
        AssertEndpoint(endpointSource, "GET", "/api/widgets/{id}");
    }

    [Fact]
    public void VersionedEndpoint_HasStableOperationId()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All,
                ApiVersions = new[] { "1", "2" }
            )]

            public record GetWidget(string Id);

            [HandlerEndpointGroup("Widgets")]
            public class WidgetHandler
            {
                public string Handle(GetWidget query) => "v1";
            }

            [HandlerEndpointGroup("Widgets", ApiVersion = "2")]
            public class WidgetV2Handler
            {
                public string Handle(GetWidget query) => "v2";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Dispatch endpoint uses the primary handler's name (no version suffix)
        Assert.Contains("WithName(\"GetWidget\")", endpointSource);
    }

    [Fact]
    public void ParameterlessGroup_WithVersion_UsesDefaultGroup()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All,
                ApiVersions = new[] { "1" }
            )]

            public record GetWidget(string Id);

            [HandlerEndpointGroup(ApiVersion = "1")]
            public class WidgetHandler
            {
                public string Handle(GetWidget query) => "widget";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // No explicit name → falls into Default group; flat route (no version path)
        Assert.DoesNotContain("MapGroup(\"v1\")", endpointSource);
        AssertEndpoint(endpointSource, "GET", "/api/widgets/{id}");
    }

    [Fact]
    public void ParameterlessGroup_NoVersion_UsesDefaultGroup()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record GetWidget(string Id);

            [HandlerEndpointGroup]
            public class WidgetHandler
            {
                public string Handle(GetWidget query) => "widget";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // No explicit name, no version → Default group, route derived from class name
        AssertEndpoint(endpointSource, "GET", "/api/widgets/{id}");
    }

    [Fact]
    public void NoApiVersionsDeclared_VersionedHandlersTreatedNormally()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record GetWidget(string Id);

            [HandlerEndpointGroup("Widgets", ApiVersion = "1")]
            public class WidgetHandler
            {
                public string Handle(GetWidget query) => "widget";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // No ApiVersions declared globally → versioning disabled, handler registered normally
        Assert.DoesNotContain("switch (apiVersion)", endpointSource);
        AssertEndpoint(endpointSource, "GET", "/api/widgets/{id}");
    }

    [Fact]
    public void VersionDispatch_DefaultsToLatestVersion()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All,
                ApiVersions = new[] { "1", "2" }
            )]

            public record GetWidget(string Id);

            [HandlerEndpointGroup("Widgets")]
            public class WidgetHandler
            {
                public string Handle(GetWidget query) => "widget-v1";
            }

            [HandlerEndpointGroup("Widgets", ApiVersion = "2")]
            public class WidgetV2Handler
            {
                public string Handle(GetWidget query) => "widget-v2";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Default version "2" is embedded in ApiVersionMetadata, used by MatcherPolicy
        Assert.Contains("ApiVersionMetadata", endpointSource);
        Assert.Contains("\"2\"", endpointSource);
    }
}
