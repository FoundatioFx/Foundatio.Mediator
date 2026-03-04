using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator.Tests;

public class EndpointGenerationTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    private static readonly MediatorGenerator Gen = new();

    /// <summary>
    /// Helper to create ASP.NET Core metadata references so the endpoint generator activates.
    /// </summary>
    private static MetadataReference[] GetAspNetCoreReferences()
    {
        var aspNetCorePath = GetAspNetCoreAssemblyPath();
        if (aspNetCorePath == null)
        {
            Assert.Skip("ASP.NET Core shared framework not found; endpoint generation tests require the ASP.NET Core SDK");
            return []; // unreachable but satisfies compiler
        }

        var assemblies = new[]
        {
            "Microsoft.AspNetCore.dll",
            "Microsoft.AspNetCore.Authorization.dll",
            "Microsoft.AspNetCore.Http.Abstractions.dll",
            "Microsoft.AspNetCore.Http.dll",
            "Microsoft.AspNetCore.Routing.dll",
            "Microsoft.AspNetCore.Routing.Abstractions.dll",
            "Microsoft.AspNetCore.Mvc.Core.dll",
            "Microsoft.AspNetCore.OpenApi.dll",
            "Microsoft.Extensions.Primitives.dll",
        };

        var refs = new List<MetadataReference>();
        foreach (var assembly in assemblies)
        {
            var path = Path.Combine(aspNetCorePath, assembly);
            if (File.Exists(path))
                refs.Add(MetadataReference.CreateFromFile(path));
        }

        if (refs.Count == 0)
        {
            Assert.Skip("No ASP.NET Core assemblies found in the shared framework directory");
            return []; // unreachable but satisfies compiler
        }

        return refs.ToArray();
    }

    private static string? GetAspNetCoreAssemblyPath()
    {
        // Find the ASP.NET Core shared framework directory
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");

        var aspNetCorePath = Path.Combine(dotnetRoot, "shared", "Microsoft.AspNetCore.App");
        if (!Directory.Exists(aspNetCorePath))
            return null;

        // Get the latest version directory
        var latestVersion = Directory.GetDirectories(aspNetCorePath)
            .OrderByDescending(d => d)
            .FirstOrDefault();

        return latestVersion;
    }

    [Fact]
    public void GlobalRoutePrefix_GeneratesNestedMapGroup()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "/api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record GetWidget(string Id);

            [HandlerCategory("Widgets")]
            public class WidgetHandler
            {
                public string Handle(GetWidget query) => "widget";
            }
            """;

        var refs = GetAspNetCoreReferences();

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        Assert.Contains("MapGroup(\"/api\")", endpointSource);
        Assert.Contains("rootGroup.MapGroup(\"/widgets\")", endpointSource);
        Assert.Contains(".WithTags(\"Widgets\")", endpointSource);
    }

    [Fact]
    public void GlobalFilters_EmittedOnRootGroup()
    {
        var source = """
            using Foundatio.Mediator;
            using Microsoft.AspNetCore.Http;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "/api",
                EndpointDiscovery = EndpointDiscovery.All,
                EndpointFilters = new[] { typeof(MyGlobalFilter) }
            )]

            public class MyGlobalFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) => next(context);
            }

            public record GetItem(string Id);

            public class ItemHandler
            {
                public string Handle(GetItem query) => "item";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        Assert.Contains("AddEndpointFilter<global::MyGlobalFilter>()", endpointSource);
        Assert.Contains("rootGroup.AddEndpointFilter<global::MyGlobalFilter>()", endpointSource);
    }

    [Fact]
    public void CategoryFilters_EmittedOnCategoryGroup()
    {
        var source = """
            using Foundatio.Mediator;
            using Microsoft.AspNetCore.Http;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public class CategoryFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) => next(context);
            }

            public record GetThing(string Id);

            [HandlerCategory("Things", EndpointFilters = [typeof(CategoryFilter)])]
            public class ThingHandler
            {
                public string Handle(GetThing query) => "thing";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        Assert.Contains("thingsGroup.AddEndpointFilter<global::CategoryFilter>()", endpointSource);
    }

    [Fact]
    public void EndpointFilters_EmittedOnIndividualRoute()
    {
        var source = """
            using Foundatio.Mediator;
            using Microsoft.AspNetCore.Http;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public class AuditFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) => next(context);
            }

            public record CreateFoo(string Name);

            public class FooHandler
            {
                [HandlerEndpoint(EndpointFilters = [typeof(AuditFilter)])]
                public string Handle(CreateFoo command) => "created";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        Assert.Contains(".AddEndpointFilter<global::AuditFilter>()", endpointSource);
    }

    [Fact]
    public void ResultOfT_ProducesAutoGenerated_Post201()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record CreateOrder(string Name);
            public record OrderView(string Id, string Name);

            public class OrderHandler
            {
                public Result<OrderView> Handle(CreateOrder command) => new OrderView("1", command.Name);
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // POST should produce 201
        Assert.Contains(".Produces<global::OrderView>(201)", endpointSource);
    }

    [Fact]
    public void ResultOfT_ProducesAutoGenerated_Get200()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetOrder(string Id);
            public record OrderView(string Id, string Name);

            public class OrderHandler
            {
                public Result<OrderView> Handle(GetOrder query) => new OrderView("1", "test");
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // GET should produce 200
        Assert.Contains(".Produces<global::OrderView>(200)", endpointSource);
    }

    [Fact]
    public void VoidHandler_NoProducesGenerated()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record DeleteItem(string Id);

            public class ItemHandler
            {
                public void Handle(DeleteItem command) { }
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        Assert.DoesNotContain(".Produces<", endpointSource);
    }

    [Fact]
    public void DiscoveryExplicit_OnlyMarkedHandlersGenerateEndpoints()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.Explicit)]

            public record GetAlpha(string Id);
            public record GetBeta(string Id);

            public class TestHandler
            {
                [HandlerEndpoint]
                public string Handle(GetAlpha query) => "alpha";

                public string Handle(GetBeta query) => "beta";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        Assert.Contains("GetAlpha", endpointSource);
        Assert.DoesNotContain("GetBeta", endpointSource);
    }

    [Fact]
    public void DiscoveryNone_NoEndpointsGenerated()
    {
        // No assembly attribute = default Discovery = None
        var source = """
            using Foundatio.Mediator;

            public record GetItem(string Id);

            public class ItemHandler
            {
                public string Handle(GetItem query) => "item";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs");

        Assert.Null(endpointSource.Source);
    }

    [Fact]
    public void AllThreeLevels_Filters_Applied()
    {
        var source = """
            using Foundatio.Mediator;
            using Microsoft.AspNetCore.Http;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "/api",
                EndpointDiscovery = EndpointDiscovery.All,
                EndpointFilters = new[] { typeof(GlobalFilter) }
            )]

            public class GlobalFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) => next(context);
            }
            public class CategoryLevelFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) => next(context);
            }
            public class EndpointLevelFilter : IEndpointFilter
            {
                public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) => next(context);
            }

            public record CreateBar(string Name);

            [HandlerCategory("Bars", EndpointFilters = [typeof(CategoryLevelFilter)])]
            public class BarHandler
            {
                [HandlerEndpoint(EndpointFilters = [typeof(EndpointLevelFilter)])]
                public string Handle(CreateBar command) => "bar";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Global filter on root group
        Assert.Contains("rootGroup.AddEndpointFilter<global::GlobalFilter>()", endpointSource);
        // Category filter on category group
        Assert.Contains("barsGroup.AddEndpointFilter<global::CategoryLevelFilter>()", endpointSource);
        // Endpoint filter on individual route
        Assert.Contains(".AddEndpointFilter<global::EndpointLevelFilter>()", endpointSource);
    }

    [Fact]
    public void GlobalRequireAuth_AppliedToRootGroup()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "/api",
                EndpointDiscovery = EndpointDiscovery.All,
                AuthorizationRequired = true
            )]

            public record GetItem(string Id);

            public class ItemHandler
            {
                public string Handle(GetItem query) => "item";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        Assert.Contains("MapGroup(\"/api\").RequireAuthorization()", endpointSource);
    }

    [Fact]
    public void CategoryRoutePrefixAutoDerivesFromName()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "/api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record GetProduct(string Id);

            [HandlerCategory("Products")]
            public class ProductHandler
            {
                public string Handle(GetProduct query) => "product";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Category name "Products" auto-derives to "/products"
        Assert.Contains("MapGroup(\"/products\")", endpointSource);
    }

    [Fact]
    public void AllowAnonymousOnMethod_EmitsAllowAnonymous()
    {
        var source = """
            using Foundatio.Mediator;
            using Microsoft.AspNetCore.Authorization;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "/api",
                EndpointDiscovery = EndpointDiscovery.All,
                AuthorizationRequired = true
            )]

            public record GetPublicInfo();
            public record GetSecretInfo();

            public class InfoHandler
            {
                [AllowAnonymous]
                public string Handle(GetPublicInfo query) => "public";

                public string Handle(GetSecretInfo query) => "secret";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Root group has RequireAuthorization
        Assert.Contains(".RequireAuthorization()", endpointSource);
        // The [AllowAnonymous] endpoint gets .AllowAnonymous()
        Assert.Contains(".AllowAnonymous()", endpointSource);
    }

    [Fact]
    public void AllowAnonymousOnClass_EmitsAllowAnonymousForAllEndpoints()
    {
        var source = """
            using Foundatio.Mediator;
            using Microsoft.AspNetCore.Authorization;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "/api",
                EndpointDiscovery = EndpointDiscovery.All,
                AuthorizationRequired = true
            )]

            public record GetHealth();
            public record GetStatus();

            [AllowAnonymous]
            public class PublicHandler
            {
                public string Handle(GetHealth query) => "healthy";
                public string Handle(GetStatus query) => "ok";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Root group has RequireAuthorization
        Assert.Contains(".RequireAuthorization()", endpointSource);
        // Both endpoints from the [AllowAnonymous] class should have .AllowAnonymous()
        var count = endpointSource!.Split(".AllowAnonymous()").Length - 1;
        Assert.Equal(2, count);
    }

    [Fact]
    public void AllowAnonymous_OverridesGlobalRoles()
    {
        var source = """
            using Foundatio.Mediator;
            using Microsoft.AspNetCore.Authorization;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "/api",
                EndpointDiscovery = EndpointDiscovery.All,
                AuthorizationRequired = true,
                AuthorizationRoles = ["Admin"]
            )]

            public record GetSecret();
            public record GetPublicInfo();

            public class SecretHandler
            {
                public string Handle(GetSecret query) => "secret";

                [AllowAnonymous]
                public string Handle(GetPublicInfo query) => "public";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Root group has role-based auth
        Assert.Contains("RequireRole(\"Admin\")", endpointSource);
        // The [AllowAnonymous] endpoint gets .AllowAnonymous() and NOT an additional RequireAuthorization
        Assert.Contains(".AllowAnonymous()", endpointSource);
        // Only one endpoint has AllowAnonymous (GetPublicInfo), not GetSecret
        var anonCount = endpointSource!.Split(".AllowAnonymous()").Length - 1;
        Assert.Equal(1, anonCount);
    }

    [Fact]
    public void FileResultHandler_GeneratesFileEndpoint()
    {
        var source = """
            using System.IO;
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "/api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record ExportReport(int Id);

            public class ReportHandler
            {
                public Result<FileResult> Handle(ExportReport query)
                    => Result.File(new MemoryStream(), "text/csv", "report.csv");
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // File results go through the result mapper which handles FileResult
        Assert.Contains("ToHttpResult(result)", endpointSource);
    }

    [Fact]
    public void TupleWithResultT_UsesInvokeAsyncAndToHttpResult()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "/api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record CreateProduct(string Name, decimal Price);
            public record ProductView(string Id, string Name, decimal Price);
            public record ProductCreated(string Id, string Name, System.DateTime CreatedAt);

            public class ProductHandler
            {
                public (Result<ProductView>, ProductCreated?) Handle(CreateProduct command)
                    => (new ProductView("1", command.Name, command.Price),
                        new ProductCreated("1", command.Name, System.DateTime.UtcNow));
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Should use mediator.InvokeAsync<Result<ProductView>> instead of calling wrapper directly
        Assert.Contains("mediator.InvokeAsync<Foundatio.Mediator.Result<ProductView>>", endpointSource);
        // Should use ToHttpResult for proper status code mapping
        Assert.Contains("ToHttpResult(result)", endpointSource);
        // Should NOT serialize the raw Result wrapper
        Assert.DoesNotContain("Results.Ok(result)", endpointSource);
    }

    [Fact]
    public void TupleWithNonGenericResult_UsesInvokeAsyncAndToHttpResult()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "/api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record DeleteProduct(string ProductId);
            public record ProductDeleted(string ProductId, System.DateTime DeletedAt);

            public class ProductHandler
            {
                public (Result, ProductDeleted?) Handle(DeleteProduct command)
                    => (Result.Success(), new ProductDeleted(command.ProductId, System.DateTime.UtcNow));
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Should use mediator.InvokeAsync<Result> for non-generic Result
        Assert.Contains("mediator.InvokeAsync<Foundatio.Mediator.Result>", endpointSource);
        // Should use ToHttpResult for proper status code mapping (e.g., 204 NoContent)
        Assert.Contains("ToHttpResult(result)", endpointSource);
    }

    [Fact]
    public void TupleWithNonResult_UsesInvokeAsyncAndResultsOk()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "/api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record CreateItem(string Name);
            public record ItemView(string Id, string Name);
            public record ItemCreated(string Id, System.DateTime CreatedAt);

            public class ItemHandler
            {
                public (ItemView, ItemCreated?) Handle(CreateItem command)
                    => (new ItemView("1", command.Name), new ItemCreated("1", System.DateTime.UtcNow));
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Should use mediator.InvokeAsync<ItemView> for non-Result tuple item
        Assert.Contains("mediator.InvokeAsync<ItemView>", endpointSource);
        // Should use Results.Ok for plain types (not Result)
        Assert.Contains("Results.Ok(result)", endpointSource);
        // The endpoint lambda should NOT call ToHttpResult (only the mapper class definition has it)
        Assert.DoesNotContain("return MediatorEndpointResultMapper", endpointSource);
    }

    [Fact]
    public void DiscoveryExplicit_HandlerCategoryTriggersEndpointGeneration()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.Explicit)]

            public record GetAlpha(string Id);
            public record GetBeta(string Id);

            [HandlerCategory("Widgets")]
            public class ExplicitCatHandler
            {
                public string Handle(GetAlpha query) => "alpha";
            }

            public class ImplicitHandler
            {
                public string Handle(GetBeta query) => "beta";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // The [HandlerCategory] handler should be included in Explicit mode
        Assert.Contains("GetAlpha", endpointSource);
        // The handler without any explicit attribute should NOT be included
        Assert.DoesNotContain("GetBeta", endpointSource);
    }

    [Fact]
    public void PostWithExplicitRoute_AllPropsInRoute_SkipsBodyBinding()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record CompleteTodo(string TodoId);

            public class TodoHandler
            {
                [HandlerEndpoint(Route = "/{todoId}/complete")]
                public Result Handle(CompleteTodo command) => Result.Success();
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Should be POST (action verb "Complete")
        Assert.Contains("MapPost", endpointSource);
        // Should NOT have [FromBody] since all properties are in the route
        Assert.DoesNotContain("FromBody", endpointSource);
        // Route should contain the placeholder
        Assert.Contains("{todoId}", endpointSource);
    }

    [Fact]
    public void ActionVerbPrefix_GeneratesCleanRouteName()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record ApproveOrder(string OrderId);

            public class OrderHandler
            {
                public Result Handle(ApproveOrder command) => Result.Success();
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Should be POST (action verb "Approve" implies write operation)
        Assert.Contains("MapPost", endpointSource);
        // Route should use the cleaned name (verb prefix "Approve" stripped from "ApproveOrder" for route)
        Assert.Contains("/order", endpointSource);
    }

    [Theory]
    [InlineData("MyApp.Orders.Api", "Orders")]
    [InlineData("Products.Module", "Products")]
    [InlineData("MyWebApp", "MyWebApp")]
    [InlineData("my-cool-api", "my_cool_api")]
    [InlineData("Api", "Api")]
    [InlineData("Company.Platform.Billing.Service", "Billing")]
    [InlineData("Web", "Web")]
    public void DeriveProjectNameFromAssembly_ProducesExpectedName(string assemblyName, string expected)
    {
        // Source-imported from src/Foundatio.Mediator/Utility/AssemblyNameHelper.cs
        var result = TestAssemblyNameHelper.DeriveProjectNameFromAssembly(assemblyName);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ProducesStatusCodes_EmitsProducesProblemCalls()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetOrder(string Id);
            public record OrderView(string Id, string Name);

            public class OrderHandler
            {
                [HandlerEndpoint(ProducesStatusCodes = [404, 422])]
                public Result<OrderView> Handle(GetOrder query) => new OrderView("1", "test");
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Success Produces should still be present
        Assert.Contains(".Produces<global::OrderView>(200)", endpointSource);
        // Error status codes from attribute
        Assert.Contains(".ProducesProblem(404)", endpointSource);
        Assert.Contains(".ProducesProblem(422)", endpointSource);
        // No other ProducesProblem should be emitted
        Assert.DoesNotContain(".ProducesProblem(400)", endpointSource);
        Assert.DoesNotContain(".ProducesProblem(500)", endpointSource);
    }

    [Fact]
    public void ProducesStatusCodes_ClassLevel_AppliesToAllMethods()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetWidget(string Id);
            public record CreateWidget(string Name);
            public record WidgetView(string Id, string Name);

            [HandlerEndpoint(ProducesStatusCodes = [400, 404, 500])]
            public class WidgetHandler
            {
                public Result<WidgetView> Handle(GetWidget query) => new WidgetView("1", "test");
                public Result<WidgetView> Handle(CreateWidget command) => new WidgetView("1", command.Name);
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Both endpoints should have ProducesProblem calls
        var problemCount400 = endpointSource!.Split(".ProducesProblem(400)").Length - 1;
        var problemCount404 = endpointSource!.Split(".ProducesProblem(404)").Length - 1;
        var problemCount500 = endpointSource!.Split(".ProducesProblem(500)").Length - 1;
        Assert.Equal(2, problemCount400);
        Assert.Equal(2, problemCount404);
        Assert.Equal(2, problemCount500);
    }

    [Fact]
    public void ProducesStatusCodes_MethodOverridesClass()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetItem(string Id);
            public record CreateItem(string Name);
            public record ItemView(string Id, string Name);

            [HandlerEndpoint(ProducesStatusCodes = [400, 500])]
            public class ItemHandler
            {
                [HandlerEndpoint(ProducesStatusCodes = [404, 422])]
                public Result<ItemView> Handle(GetItem query) => new ItemView("1", "test");

                public Result<ItemView> Handle(CreateItem command) => new ItemView("1", command.Name);
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // GetItem (method override) should have 404 and 422, NOT 400 and 500
        Assert.Contains(".ProducesProblem(404)", endpointSource);
        Assert.Contains(".ProducesProblem(422)", endpointSource);
        // CreateItem (class-level fallback) should have 400 and 500
        Assert.Contains(".ProducesProblem(400)", endpointSource);
        Assert.Contains(".ProducesProblem(500)", endpointSource);
    }

    [Fact]
    public void NoProducesStatusCodes_NoProducesProblemEmitted()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetThing(string Id);

            public class ThingHandler
            {
                public Result<string> Handle(GetThing query) => "thing";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Success Produces should still be present
        Assert.Contains(".Produces<", endpointSource);
        // No ProducesProblem without the attribute and no Result factory calls
        Assert.DoesNotContain(".ProducesProblem(", endpointSource);
    }

    [Fact]
    public void AutoDetect_ResultFactoryCalls_EmitsProducesProblem()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetOrder(string Id);
            public record OrderView(string Id, string Name);

            public class OrderHandler
            {
                public Result<OrderView> Handle(GetOrder query)
                {
                    if (query.Id == null)
                        return Result<OrderView>.NotFound("Order not found");

                    if (query.Id == "bad")
                        return Result<OrderView>.Invalid("Invalid ID");

                    return new OrderView(query.Id, "Test");
                }
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Auto-detected from method body
        Assert.Contains(".ProducesProblem(404)", endpointSource); // NotFound
        Assert.Contains(".ProducesProblem(422)", endpointSource); // Invalid
        // Should NOT contain codes not used in the method
        Assert.DoesNotContain(".ProducesProblem(400)", endpointSource);
        Assert.DoesNotContain(".ProducesProblem(500)", endpointSource);
        Assert.DoesNotContain(".ProducesProblem(409)", endpointSource);
    }

    [Fact]
    public void AutoDetect_NonGenericResult_EmitsProducesProblem()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record DeleteOrder(string Id);

            public class OrderHandler
            {
                public Result Handle(DeleteOrder command)
                {
                    if (command.Id == null)
                        return Result.NotFound("Order not found");

                    if (command.Id == "conflict")
                        return Result.Conflict("Order is in use");

                    return Result.Success();
                }
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        Assert.Contains(".ProducesProblem(404)", endpointSource); // NotFound
        Assert.Contains(".ProducesProblem(409)", endpointSource); // Conflict
        Assert.DoesNotContain(".ProducesProblem(422)", endpointSource);
    }

    [Fact]
    public void ExplicitAttribute_OverridesAutoDetection()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetItem(string Id);
            public record ItemView(string Id);

            public class ItemHandler
            {
                [HandlerEndpoint(ProducesStatusCodes = [400])]
                public Result<ItemView> Handle(GetItem query)
                {
                    if (query.Id == null)
                        return Result<ItemView>.NotFound("Not found");

                    return new ItemView(query.Id);
                }
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Explicit attribute wins — only 400, NOT the auto-detected 404
        Assert.Contains(".ProducesProblem(400)", endpointSource);
        Assert.DoesNotContain(".ProducesProblem(404)", endpointSource);
    }

    [Fact]
    public void AutoDetect_NonResultReturn_NoProducesProblem()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetName(string Id);

            public class NameHandler
            {
                public string Handle(GetName query) => "hello";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Non-Result return type — no ProducesProblem at all
        Assert.DoesNotContain(".ProducesProblem(", endpointSource);
    }
}

/// <summary>
/// Mirrors <c>AssemblyNameHelper.DeriveProjectNameFromAssembly</c> from the generator project
/// so we can unit-test the algorithm without exposing internal types across a strong-name boundary.
/// Source of truth: src/Foundatio.Mediator/Utility/AssemblyNameHelper.cs
/// </summary>
file static class TestAssemblyNameHelper
{
    internal static string DeriveProjectNameFromAssembly(string assemblyName)
    {
        var segments = assemblyName.Split('.');
        string[] stripSuffixes = ["Api", "Web", "Module", "Service", "Server", "Host", "App"];

        for (int i = segments.Length - 1; i >= 0; i--)
        {
            var segment = segments[i].Trim();
            if (string.IsNullOrEmpty(segment))
                continue;

            bool isSuffix = false;
            foreach (var suffix in stripSuffixes)
            {
                if (string.Equals(segment, suffix, StringComparison.OrdinalIgnoreCase))
                {
                    isSuffix = true;
                    break;
                }
            }

            if (!isSuffix)
                return Sanitize(segment.Replace("-", "_"));
        }

        return Sanitize(assemblyName.Replace(".", "_").Replace("-", "_"));
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var id = new string(name.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        return id.Length > 0 && char.IsDigit(id[0]) ? "_" + id : id;
    }
}
