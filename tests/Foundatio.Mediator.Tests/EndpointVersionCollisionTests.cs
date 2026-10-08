namespace Foundatio.Mediator.Tests;

public class EndpointVersionCollisionTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Theory]
    [InlineData("orders", "products", "/api/orders/items", "/api/products/items")]
    [InlineData("/orders", "/products", "/orders/items", "/products/items")]
    [InlineData("/orders", "orders", "/orders/items", "/api/orders/items")]
    public void SameRelativeRoute_DistinctGroupPaths_DoesNotReportOverlap(
        string firstPrefix, string secondPrefix, string firstPath, string secondPath)
    {
        // Arrange
        var refs = GetAspNetCoreReferences();
        Assert.NotEmpty(refs);

        // Act
        var result = RunGenerator(CreateSource(firstPrefix, secondPrefix, "items"),
            [new MediatorGenerator()], additionalReferences: refs, assertCleanCompilation: false);

        // Assert
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "FMED017");
        var endpointSource = Assert.Single(result.GeneratedTrees,
            tree => tree.HintName == "_MediatorEndpoints.g.cs").Source;
        AssertEndpoint(endpointSource, "GET", firstPath);
        AssertEndpoint(endpointSource, "GET", secondPath);
    }

    [Theory]
    [InlineData("orders", "orders", "items", "Products")]
    [InlineData("orders", "products", "/items", "Products")]
    [InlineData("orders", "products", "items", "Orders")]
    [InlineData("{tenant}", "{customer}", "items", "Products")]
    public void SameMappedPath_OverlappingVersions_ReportsOverlap(
        string firstPrefix, string secondPrefix, string route, string secondGroup)
    {
        // Arrange
        var refs = GetAspNetCoreReferences();
        Assert.NotEmpty(refs);

        // Act
        var result = RunGenerator(CreateSource(firstPrefix, secondPrefix, route, secondGroup: secondGroup),
            [new MediatorGenerator()], additionalReferences: refs, assertCleanCompilation: false);

        // Assert
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Id == "FMED017");
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, diagnostic.Severity);
    }

    [Fact]
    public void SameMappedPath_DisjointVersions_DoesNotReportOverlap()
    {
        // Arrange
        var refs = GetAspNetCoreReferences();
        Assert.NotEmpty(refs);

        // Act
        var result = RunGenerator(CreateSource("orders", "orders", "items", secondVersion: "2"),
            [new MediatorGenerator()], additionalReferences: refs, assertCleanCompilation: false);

        // Assert
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Id == "FMED017");
        var endpointSource = Assert.Single(result.GeneratedTrees,
            tree => tree.HintName == "_MediatorEndpoints.g.cs").Source;
        AssertEndpoint(endpointSource, "GET", "/api/orders/items");
    }

    [Fact]
    public void SharedMappedPath_InconsistentGroupPrefixes_ReportsOverlap()
    {
        // Arrange
        var refs = GetAspNetCoreReferences();
        Assert.NotEmpty(refs);
        var source = """
            using Foundatio.Mediator;
            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All,
                ApiVersions = new[] { "1" })]

            public record GetOtherItems;
            public record GetSecondItems;
            public record GetThirdItems;

            [HandlerEndpointGroup("Orders", RoutePrefix = "shared", ApiVersion = "1")]
            public class FirstOrderHandler
            {
                [HandlerEndpoint(Route = "other", HttpMethod = EndpointHttpMethod.Get)]
                public string Handle(GetOtherItems query) => "first";
            }

            [HandlerEndpointGroup("Orders", RoutePrefix = "orders", ApiVersion = "1")]
            public class SecondOrderHandler
            {
                [HandlerEndpoint(Route = "items", HttpMethod = EndpointHttpMethod.Get)]
                public string Handle(GetSecondItems query) => "second";
            }

            [HandlerEndpointGroup("Products", RoutePrefix = "shared", ApiVersion = "1")]
            public class ThirdProductHandler
            {
                [HandlerEndpoint(Route = "items", HttpMethod = EndpointHttpMethod.Get)]
                public string Handle(GetThirdItems query) => "third";
            }
            """;

        // Act
        var result = RunGenerator(source, [new MediatorGenerator()],
            additionalReferences: refs, assertCleanCompilation: false);

        // Assert
        var endpointSource = Assert.Single(result.GeneratedTrees,
            tree => tree.HintName == "_MediatorEndpoints.g.cs").Source;
        Assert.Equal(2, ParseEndpointLogEntries(endpointSource)
            .Count(endpoint => endpoint.Method == "GET" && endpoint.Route == "/api/shared/items"));
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Id == "FMED017");
        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, diagnostic.Severity);
    }

    private static string CreateSource(string firstPrefix, string secondPrefix, string route,
        string secondVersion = "1", string secondGroup = "Products") => $$"""
        using Foundatio.Mediator;
        [assembly: MediatorConfiguration(
            EndpointRoutePrefix = "api",
            EndpointDiscovery = EndpointDiscovery.All,
            ApiVersions = new[] { "1", "2" })]

        public record GetOrderItems;
        public record GetProductItems;

        [HandlerEndpointGroup("Orders", RoutePrefix = "{{firstPrefix}}", ApiVersion = "1")]
        public class OrderHandler
        {
            [HandlerEndpoint(Route = "{{route}}", HttpMethod = EndpointHttpMethod.Get)]
            public string Handle(GetOrderItems query) => "orders";
        }

        [HandlerEndpointGroup("{{secondGroup}}", RoutePrefix = "{{secondPrefix}}", ApiVersion = "{{secondVersion}}")]
        public class ProductHandler
        {
            [HandlerEndpoint(Route = "{{route}}", HttpMethod = EndpointHttpMethod.Get)]
            public string Handle(GetProductItems query) => "products";
        }
        """;
}
