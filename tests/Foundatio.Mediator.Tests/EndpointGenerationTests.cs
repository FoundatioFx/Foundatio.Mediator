using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator.Tests;

public class EndpointGenerationTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    private static readonly MediatorGenerator Gen = new();

    [Fact]
    public void GlobalRoutePrefix_GeneratesNestedMapGroup()
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

        var refs = GetAspNetCoreReferences();

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        Assert.Contains("MapGroup(\"api\")", endpointSource);
        Assert.Contains("rootGroup.MapGroup(\"widgets\")", endpointSource);
        Assert.Contains(".WithTags(\"Widgets\")", endpointSource);
    }

    [Fact]
    public void GlobalFilters_EmittedOnRootGroup()
    {
        var source = """
            using Foundatio.Mediator;
            using Microsoft.AspNetCore.Http;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
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

            [HandlerEndpointGroup("Things", EndpointFilters = [typeof(CategoryFilter)])]
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
    public void ResultOfT_PostWithoutCreated_Produces200()
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
        // POST without Result.Created() should produce 200, not 201
        Assert.Contains(".Produces<global::OrderView>(200)", endpointSource);
        Assert.DoesNotContain("201", endpointSource);
    }

    [Fact]
    public void ResultOfT_PostWithResultCreated_Produces201()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record CreateOrder(string Name);
            public record OrderView(string Id, string Name);

            public class OrderHandler
            {
                public Result<OrderView> Handle(CreateOrder command)
                {
                    var order = new OrderView("1", command.Name);
                    return Result.Created(order);
                }
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // POST with Result.Created() should produce 201
        Assert.Contains(".Produces<global::OrderView>(201)", endpointSource);
    }

    [Fact]
    public void ResultOfT_ExplicitSuccessStatusCode_OverridesAutoDetection()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record CreateOrder(string Name);
            public record OrderView(string Id, string Name);

            public class OrderHandler
            {
                [HandlerEndpoint(SuccessStatusCode = 201)]
                public Result<OrderView> Handle(CreateOrder command) => new OrderView("1", command.Name);
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Explicit SuccessStatusCode = 201 should override auto-detection (which would give 200)
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
        // Explicitly set Discovery = None
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.None)]

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
    public void DesignTimeStub_GeneratedForIntelliSense()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetWidget(string Id);

            public class WidgetHandler
            {
                public string Handle(GetWidget query) => "widget";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);

        // Stub file should be generated (this is what IntelliSense sees at design time)
        var stubSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.Api.g.cs").Source;
        Assert.NotNull(stubSource);
        Assert.Contains("public static partial class", stubSource);
        Assert.Contains("Tests_MediatorEndpoints", stubSource);
        Assert.Contains("static partial void MapEndpointsCore", stubSource);

        // Implementation file should also be generated (compile-time only in real IDE, but test runs both)
        var implSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;
        Assert.NotNull(implSource);
        Assert.Contains("static partial void MapEndpointsCore(IEndpointRouteBuilder endpoints, bool logEndpoints)", implSource);
        Assert.Contains("MapGet", implSource);
        Assert.DoesNotContain("MediatorEndpointModule", implSource);
    }

    [Fact]
    public void DesignTimeStub_NotGeneratedWhenDiscoveryNone()
    {
        // Explicitly set Discovery = None
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.None)]

            public record GetItem(string Id);

            public class ItemHandler
            {
                public string Handle(GetItem query) => "item";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);

        // Neither stub nor implementation should be generated when discovery is None
        var stubSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.Api.g.cs");
        Assert.Null(stubSource.Source);
        var implSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs");
        Assert.Null(implSource.Source);
    }

    [Fact]
    public void AllThreeLevels_Filters_Applied()
    {
        var source = """
            using Foundatio.Mediator;
            using Microsoft.AspNetCore.Http;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
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

            [HandlerEndpointGroup("Bars", EndpointFilters = [typeof(CategoryLevelFilter)])]
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
                EndpointRoutePrefix = "api",
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
        Assert.Contains("MapGroup(\"api\").RequireAuthorization()", endpointSource);
    }

    [Fact]
    public void CategoryRoutePrefixAutoDerivesFromName()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record GetProduct(string Id);

            [HandlerEndpointGroup("Products")]
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
        // Category name "Products" auto-derives to "products" (relative, no leading /)
        Assert.Contains("MapGroup(\"products\")", endpointSource);
    }

    [Fact]
    public void AllowAnonymousOnMethod_EmitsAllowAnonymous()
    {
        var source = """
            using Foundatio.Mediator;
            using Microsoft.AspNetCore.Authorization;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
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
                EndpointRoutePrefix = "api",
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
                EndpointRoutePrefix = "api",
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
                EndpointRoutePrefix = "api",
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
                EndpointRoutePrefix = "api",
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
                EndpointRoutePrefix = "api",
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
                EndpointRoutePrefix = "api",
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
    public void DiscoveryExplicit_HandlerEndpointGroupTriggersEndpointGeneration()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.Explicit)]

            public record GetAlpha(string Id);
            public record GetBeta(string Id);

            [HandlerEndpointGroup("Widgets")]
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
        // The [HandlerEndpointGroup] handler should be included in Explicit mode
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
    public void ActionVerbPrefix_GeneratesActionRouteWithId()
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
        // Route should include entity name, ID route param, and action verb suffix
        // ApproveOrder(string OrderId) → POST /orders/{orderId}/approve
        Assert.Contains("/orders/{orderId}/approve", endpointSource);
        // All properties covered by route — no body binding
        Assert.DoesNotContain("FromBody", endpointSource);
    }

    [Fact]
    public void ActionVerbWithCategory_GeneratesActionRoute()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record CompleteTodo(string TodoId);
            public record ArchiveTodo(string TodoId);
            public record CreateTodo(string Name);

            [HandlerEndpointGroup("Todos")]
            public class TodoHandler
            {
                public Result Handle(CompleteTodo command) => Result.Success();
                public Result Handle(ArchiveTodo command) => Result.Success();
                public Result Handle(CreateTodo command) => Result.Success();
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // CompleteTodo → POST /api/todos/{todoId}/complete
        Assert.Contains("{todoId}/complete", endpointSource);
        // ArchiveTodo → POST /api/todos/{todoId}/archive
        Assert.Contains("{todoId}/archive", endpointSource);
        // CreateTodo → POST /api/todos/ (CRUD verb, no action suffix)
        // Should NOT have "create" as a route suffix
        Assert.DoesNotContain("/create", endpointSource);
    }

    [Fact]
    public void ActionVerbNoId_GeneratesActionRouteWithoutRouteParam()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record ResetCounters;

            [HandlerEndpointGroup("Counters")]
            public class CounterHandler
            {
                public Result Handle(ResetCounters command) => Result.Success();
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // ResetCounters in "Counters" category → POST /api/counters/reset
        Assert.Contains("MapPost", endpointSource);
        Assert.Contains("/reset", endpointSource);
    }

    [Fact]
    public void ActionVerbWithExtraProps_KeepsBodyBinding()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record RejectOrder(string OrderId, string Reason);

            [HandlerEndpointGroup("Orders")]
            public class OrderHandler
            {
                public Result Handle(RejectOrder command) => Result.Success();
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // RejectOrder → POST /api/orders/{orderId}/reject
        Assert.Contains("{orderId}/reject", endpointSource);
        // Has non-ID property (Reason) so body binding should remain
        Assert.Contains("FromBody", endpointSource);
    }

    [Fact]
    public void ActionVerbMismatchedEntity_PreservesEntityInRoute()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record CompleteSomething(string SomethingId);
            public record CompleteTodo(string TodoId);

            [HandlerEndpointGroup("Todos")]
            public class TodoHandler
            {
                public Result Handle(CompleteSomething command) => Result.Success();
                public Result Handle(CompleteTodo command) => Result.Success();
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // CompleteSomething — entity "Something" doesn't match category "Todos"
        // should preserve entity: /{somethingId}/complete-something
        Assert.Contains("{somethingId}/complete-something", endpointSource);
        // CompleteTodo — entity "Todo" matches category "Todos"
        // should use clean action: /{todoId}/complete
        Assert.Contains("{todoId}/complete\"", endpointSource);
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
        Assert.Contains(".ProducesProblem(400)", endpointSource); // Invalid
        // Should NOT contain codes not used in the method
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

    [Fact]
    public void FMED015_CategoryPrefixStartsWithGlobalPrefix_EmitsWarning()
    {
        // Relative prefix (no leading /) that duplicates the global prefix content
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record GetOrder(string Id);

            [HandlerEndpointGroup("Orders", RoutePrefix = "api/orders")]
            public class OrderHandler
            {
                public string Handle(GetOrder query) => "order";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, diagnostics, _) = RunGenerator(source, [Gen], additionalReferences: refs);

        var warning = diagnostics.FirstOrDefault(d => d.Id == "FMED015");
        Assert.NotNull(warning);
        Assert.Equal(DiagnosticSeverity.Warning, warning!.Severity);
        Assert.Contains("api/orders", warning.GetMessage());
        Assert.Contains("orders", warning.GetMessage()); // suggests the trimmed prefix
    }

    [Fact]
    public void FMED015_AbsoluteCategoryPrefix_NoWarning()
    {
        // A leading / makes the category absolute (bypasses global prefix), so no doubling
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record GetOrder(string Id);

            [HandlerEndpointGroup("Orders", RoutePrefix = "/orders")]
            public class OrderHandler
            {
                public string Handle(GetOrder query) => "order";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, diagnostics, _) = RunGenerator(source, [Gen], additionalReferences: refs);

        var warning = diagnostics.FirstOrDefault(d => d.Id == "FMED015");
        Assert.Null(warning);
    }

    [Fact]
    public void FMED015_NoGlobalPrefix_NoWarning()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record GetOrder(string Id);

            [HandlerEndpointGroup("Orders", RoutePrefix = "/api/orders")]
            public class OrderHandler
            {
                public string Handle(GetOrder query) => "order";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, diagnostics, _) = RunGenerator(source, [Gen], additionalReferences: refs);

        var warning = diagnostics.FirstOrDefault(d => d.Id == "FMED015");
        Assert.Null(warning);
    }

    [Fact]
    public void FMED015_ExactMatchOfGlobalPrefix_NoWarning()
    {
        // Category prefix that exactly equals the global prefix is unusual but not a "double-up"
        // because the generated path is just /api (not /api/api)
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record GetOrder(string Id);

            [HandlerEndpointGroup("Orders", RoutePrefix = "/api")]
            public class OrderHandler
            {
                public string Handle(GetOrder query) => "order";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, diagnostics, _) = RunGenerator(source, [Gen], additionalReferences: refs);

        var warning = diagnostics.FirstOrDefault(d => d.Id == "FMED015");
        Assert.Null(warning);
    }

    [Fact]
    public void AbsolutePrefix_CategoryRouteBypassesGlobalPrefix()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record GetHealth();

            [HandlerEndpointGroup("Health", RoutePrefix = "/health")]
            public class HealthHandler
            {
                public string Handle(GetHealth query) => "ok";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, diagnostics, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Category group should be parented on `endpoints`, not `rootGroup`
        Assert.Contains("endpoints.MapGroup(\"/health\")", endpointSource);
        // Global prefix group should still exist
        Assert.Contains("MapGroup(\"api\")", endpointSource);
        // No FMED015 warning for absolute route
        Assert.Null(diagnostics.FirstOrDefault(d => d.Id == "FMED015"));
    }

    [Fact]
    public void AbsoluteRoute_ExplicitRouteBypassesAllPrefixes()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record GetStatus();

            [HandlerEndpointGroup("Products", RoutePrefix = "products")]
            public class ProductHandler
            {
                [HandlerEndpoint(Route = "/status")]
                public string Handle(GetStatus query) => "ok";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // The endpoint should be registered directly on `endpoints`, not the category group
        Assert.Contains("endpoints.MapGet(\"/status\"", endpointSource);
    }

    [Fact]
    public void RelativePrefix_CategoryNestedUnderGlobalGroup()
    {
        // Verify that without leading / the category is nested under the global group
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointRoutePrefix = "api",
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record GetHealth();

            [HandlerEndpointGroup("Health", RoutePrefix = "health")]
            public class HealthHandler
            {
                public string Handle(GetHealth query) => "ok";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Category group should be parented on `rootGroup` (relative behavior)
        Assert.Contains("rootGroup.MapGroup(\"health\")", endpointSource);
    }

    [Fact]
    public void StreamingEndpoint_DefaultFormat_ReturnsOkWithStream()
    {
        var source = """
            using Foundatio.Mediator;
            using System.Collections.Generic;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetEventStream;

            public class EventStreamHandler
            {
                public async IAsyncEnumerable<string> HandleAsync(GetEventStream query) { yield return "event"; }
            }
            """;

        var refs = GetAspNetCoreReferences();

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Default streaming endpoints should use GET
        Assert.Contains("MapGet(", endpointSource);
        // Should return Ok(stream) for default JSON array streaming
        Assert.Contains("Results.Ok(stream)", endpointSource);
    }

    [Fact]
    public void StreamingEndpoint_SseFormat_UsesTypedResultsServerSentEvents()
    {
        var source = """
            using Foundatio.Mediator;
            using System.Collections.Generic;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetEventStream;

            public class EventStreamHandler
            {
                [HandlerEndpoint(Streaming = EndpointStreaming.ServerSentEvents, SseEventType = "event")]
                public async IAsyncEnumerable<string> HandleAsync(GetEventStream query) { yield return "event"; }
            }
            """;

        var refs = GetAspNetCoreReferences();

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // SSE streaming endpoints should use GET
        Assert.Contains("MapGet(", endpointSource);
        // Should use TypedResults.ServerSentEvents
        Assert.Contains("TypedResults.ServerSentEvents(stream", endpointSource);
        Assert.Contains("eventType: \"event\"", endpointSource);
        // Should include SSE using
        Assert.Contains("using System.Net.ServerSentEvents;", endpointSource);
        // Should have SSE content type metadata
        Assert.Contains("text/event-stream", endpointSource);
    }

    [Fact]
    public void StreamingEndpoint_SseFormat_WithoutEventType()
    {
        var source = """
            using Foundatio.Mediator;
            using System.Collections.Generic;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetUpdates;

            public class UpdateStreamHandler
            {
                [HandlerEndpoint(Streaming = EndpointStreaming.ServerSentEvents)]
                public async IAsyncEnumerable<string> HandleAsync(GetUpdates query) { yield return "update"; }
            }
            """;

        var refs = GetAspNetCoreReferences();

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Should use TypedResults.ServerSentEvents without eventType
        Assert.Contains("TypedResults.ServerSentEvents(stream)", endpointSource);
        // Should NOT contain eventType parameter
        Assert.DoesNotContain("eventType:", endpointSource);
    }

    [Fact]
    public void EndpointSummaryStyle_Spaced_SplitsPascalCase()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointDiscovery = EndpointDiscovery.All,
                EndpointSummaryStyle = EndpointSummaryStyle.Spaced
            )]

            public record CreateTodo(string Title);

            [HandlerEndpointGroup("Todos")]
            public class TodoHandler
            {
                public string Handle(CreateTodo cmd) => "done";
            }
            """;

        var refs = GetAspNetCoreReferences();

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        Assert.Contains(".WithSummary(\"Create Todo\")", endpointSource);
        Assert.DoesNotContain(".WithSummary(\"CreateTodo\")", endpointSource);
    }

    [Fact]
    public void EndpointSummaryStyle_Exact_KeepsPascalCase()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointDiscovery = EndpointDiscovery.All,
                EndpointSummaryStyle = EndpointSummaryStyle.Exact
            )]

            public record CreateTodo(string Title);

            [HandlerEndpointGroup("Todos")]
            public class TodoHandler
            {
                public string Handle(CreateTodo cmd) => "done";
            }
            """;

        var refs = GetAspNetCoreReferences();

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        Assert.Contains(".WithSummary(\"CreateTodo\")", endpointSource);
    }

    [Fact]
    public void EndpointSummaryStyle_Default_KeepsPascalCase()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(
                EndpointDiscovery = EndpointDiscovery.All
            )]

            public record GetProductDetails(string Id);

            [HandlerEndpointGroup("Products")]
            public class ProductHandler
            {
                public string Handle(GetProductDetails query) => "product";
            }
            """;

        var refs = GetAspNetCoreReferences();

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        Assert.Contains(".WithSummary(\"GetProductDetails\")", endpointSource);
    }

    [Fact]
    public void SingularAndPluralMessages_ProduceSamePluralRoute()
    {
        // GetTodo and GetTodos should both generate routes under /todos
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetTodo(string Id);
            public record GetTodos();

            public class TodoHandler
            {
                public string Handle(GetTodo query) => "one";
                public string[] Handle(GetTodos query) => [];
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // GetTodo should pluralize to /todos/{id}
        Assert.Contains("/todos/{id}", endpointSource);
        // GetTodos is already plural → /todos
        Assert.Contains("MapGet(\"/todos\"", endpointSource);
    }

    [Fact]
    public void UncountableNoun_RouteNotPluralized()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetHealth();

            public class HealthHandler
            {
                public string Handle(GetHealth query) => "ok";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Health is uncountable — should stay /health, not /healths
        Assert.Contains("\"/health\"", endpointSource);
        Assert.DoesNotContain("healths", endpointSource);
    }

    [Fact]
    public void IrregularNoun_RoutePluralizesCorrectly()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetPerson(string Id);

            public class PersonHandler
            {
                public string Handle(GetPerson query) => "person";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Person → People (irregular)
        Assert.Contains("/people/{id}", endpointSource);
        Assert.DoesNotContain("/persons", endpointSource);
    }

    [Fact]
    public void ConsonantY_PluralizesToIes()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetCategory(string Id);

            public class CategoryHandler
            {
                public string Handle(GetCategory query) => "cat";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Category → Categories (consonant + y → ies)
        Assert.Contains("/categories/{id}", endpointSource);
    }

    [Fact]
    public void VowelY_PluralizesWithS()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetKey(string Id);

            public class KeyHandler
            {
                public string Handle(GetKey query) => "key";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Key → Keys (vowel + y → just s)
        Assert.Contains("/keys/{id}", endpointSource);
    }

    [Fact]
    public void SibilantEnding_PluralizesWithEs()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetBatch(string Id);

            public class BatchHandler
            {
                public string Handle(GetBatch query) => "batch";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // Batch → Batches (ch → es)
        Assert.Contains("/batches/{id}", endpointSource);
    }

    [Fact]
    public void GetAllTodos_NormalizesToTodosRoute()
    {
        // GetAllTodos should strip "All" qualifier and produce /todos, not /all-todos
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetAllTodos();
            public record GetTodo(string Id);

            public class TodoHandler
            {
                public string[] Handle(GetAllTodos query) => [];
                public string Handle(GetTodo query) => "todo";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // GetAllTodos → strip Get → AllTodos → strip All → Todos → /todos
        Assert.Contains("MapGet(\"/todos\"", endpointSource);
        // GetTodo → strip Get → Todo → pluralize → Todos → /todos/{id}
        Assert.Contains("/todos/{id}", endpointSource);
        // Should NOT produce /all-todos
        Assert.DoesNotContain("all-todos", endpointSource);
    }

    [Fact]
    public void GetTodoById_NormalizesToTodosRoute()
    {
        // GetTodoById should strip "ById" suffix and produce /todos/{id}
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetTodoById(string Id);

            public class TodoHandler
            {
                public string Handle(GetTodoById query) => "todo";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // GetTodoById → strip Get → TodoById → strip ById → Todo → pluralize → /todos/{id}
        Assert.Contains("/todos/{id}", endpointSource);
        Assert.DoesNotContain("todo-by-id", endpointSource);
    }

    [Fact]
    public void GetTodoByName_NormalizesToTodosRoute()
    {
        // GetTodoByName should strip "ByName" suffix
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetTodoByName(string Name);

            public class TodoHandler
            {
                public string Handle(GetTodoByName query) => "todo";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // GetTodoByName → strip Get → TodoByName → extract By<Property> → entity "Todo", suffix "by-name"
        // → pluralize entity → /todos/by-name
        Assert.Contains("/todos/by-name", endpointSource);
    }

    [Fact]
    public void GetOrderDetails_NormalizesToOrdersRoute()
    {
        // GetOrderDetails should strip "Details" suffix
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetOrderDetails(string Id);

            public class OrderHandler
            {
                public string Handle(GetOrderDetails query) => "order";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // GetOrderDetails → strip Get → OrderDetails → strip Details → Order → pluralize → /orders/{id}
        Assert.Contains("/orders/{id}", endpointSource);
        Assert.DoesNotContain("order-details", endpointSource);
    }

    [Fact]
    public void FMED016_DivergentRoutes_EmitsWarning()
    {
        // When a handler class without [HandlerEndpointGroup] produces different route prefixes,
        // emit FMED016 warning
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetTodo(string Id);
            public record GetStatus();

            public class TodoHandler
            {
                public string Handle(GetTodo query) => "todo";
                public string Handle(GetStatus query) => "ok";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, diagnostics, _) = RunGenerator(source, [Gen], additionalReferences: refs);

        var warning = diagnostics.FirstOrDefault(d => d.Id == "FMED016");
        Assert.NotNull(warning);
        Assert.Equal(DiagnosticSeverity.Warning, warning!.Severity);
        Assert.Contains("TodoHandler", warning.GetMessage());
    }

    [Fact]
    public void FMED016_ConsistentRoutes_NoWarning()
    {
        // Handlers with consistent entity names should not trigger FMED016
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetTodo(string Id);
            public record CreateTodo(string Title);
            public record GetAllTodos();

            public class TodoHandler
            {
                public string Handle(GetTodo query) => "todo";
                public string Handle(CreateTodo cmd) => "created";
                public string[] Handle(GetAllTodos query) => [];
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, diagnostics, _) = RunGenerator(source, [Gen], additionalReferences: refs);

        var warning = diagnostics.FirstOrDefault(d => d.Id == "FMED016");
        Assert.Null(warning);
    }

    [Fact]
    public void FMED016_WithExplicitGroup_NoWarning()
    {
        // Handlers with [HandlerEndpointGroup] should not trigger FMED016 even with different message names
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetTodo(string Id);
            public record GetStatus();

            [HandlerEndpointGroup("Todos")]
            public class TodoHandler
            {
                public string Handle(GetTodo query) => "todo";
                public string Handle(GetStatus query) => "ok";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, diagnostics, _) = RunGenerator(source, [Gen], additionalReferences: refs);

        var warning = diagnostics.FirstOrDefault(d => d.Id == "FMED016");
        Assert.Null(warning);
    }

    [Fact]
    public void GetTodoItemsWithPagination_StripsWithFeature()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetTodoItemsWithPagination(int Page, int PageSize);

            public class TodoHandler
            {
                public string Handle(GetTodoItemsWithPagination query) => "todos";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // GetTodoItemsWithPagination → strip Get → TodoItemsWithPagination → strip With... → TodoItems → /todo-items
        Assert.Contains("/todo-items", endpointSource);
        Assert.DoesNotContain("pagination", endpointSource);
    }

    [Fact]
    public void GetProductsPaged_StripsPagedSuffix()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetProductsPaged(int Page);

            public class ProductHandler
            {
                public string Handle(GetProductsPaged query) => "products";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // GetProductsPaged → strip Get → ProductsPaged → strip Paged → Products → /products
        Assert.Contains("/products", endpointSource);
        Assert.DoesNotContain("paged", endpointSource);
    }

    [Fact]
    public void GetOrderCount_ProducesCountRouteSuffix()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetOrderCount();

            public class OrderHandler
            {
                public int Handle(GetOrderCount query) => 42;
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // GetOrderCount → strip Get → OrderCount → entity "Order", suffix "count" → /orders/count
        Assert.Contains("/orders/count", endpointSource);
    }

    [Fact]
    public void GetOrdersForCustomer_ProducesForRouteSuffix()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetOrdersForCustomer(string CustomerId);

            public class OrderHandler
            {
                public string Handle(GetOrdersForCustomer query) => "orders";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // GetOrdersForCustomer → strip Get → OrdersForCustomer → entity "Orders", suffix "for-customer"
        // → /orders/for-customer/{customerId}
        Assert.Contains("/orders/for-customer", endpointSource);
    }

    [Fact]
    public void GetOrdersFromUser_ProducesFromRouteSuffix()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetOrdersFromUser(string UserId);

            public class OrderHandler
            {
                public string Handle(GetOrdersFromUser query) => "orders";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // GetOrdersFromUser → strip Get → OrdersFromUser → entity "Orders", suffix "from-user"
        // → /orders/from-user/{userId}
        Assert.Contains("/orders/from-user", endpointSource);
    }

    [Fact]
    public void ExportOrders_ProducesActionRoute()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record ExportOrders();

            public class OrderHandler
            {
                public string Handle(ExportOrders command) => "csv";
            }
            """;

        var refs = GetAspNetCoreReferences();
        if (refs.Length == 0) return;

        var (_, _, trees) = RunGenerator(source, [Gen], additionalReferences: refs);
        var endpointSource = trees.FirstOrDefault(t => t.HintName == "_MediatorEndpoints.g.cs").Source;

        Assert.NotNull(endpointSource);
        // ExportOrders → action verb "export", entity "Orders" → POST /orders/export
        Assert.Contains("/orders/export", endpointSource);
        Assert.Contains("MapPost", endpointSource);
    }
}

