using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator.Tests;

/// <summary>
/// Regression tests for endpoint route convention generation.
/// Verifies CRUD routes, verb prefix inference, action verbs, entity name normalization,
/// pluralization, and route parameter detection all produce expected routes.
/// </summary>
public class EndpointRouteConventionTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Fact]
    public void SingleHandler_AllCrudMessages_GeneratesCorrectRoutes()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetTodo(string TodoId);
            public record GetAllTodos();
            public record CreateTodo(string Name);
            public record UpdateTodo(string TodoId, string Name);
            public record DeleteTodo(string TodoId);

            public class TodoHandler
            {
                public string Handle(GetTodo query) => "todo";
                public string[] Handle(GetAllTodos query) => [];
                public string Handle(CreateTodo command) => "created";
                public string Handle(UpdateTodo command) => "updated";
                public void Handle(DeleteTodo command) { }
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/todos/{todoId}");
        AssertEndpoint(endpointSource, "GET", "/api/todos");
        AssertEndpoint(endpointSource, "POST", "/api/todos");
        AssertEndpoint(endpointSource, "PUT", "/api/todos/{todoId}");
        AssertEndpoint(endpointSource, "DELETE", "/api/todos/{todoId}");

        // CRUD verbs should NOT appear as route suffixes
        AssertNoRouteContains(endpointSource, "/create");
        AssertNoRouteContains(endpointSource, "/update");
        AssertNoRouteContains(endpointSource, "/delete");
    }

    [Fact]
    public void ActionVerb_WithCrudMessages_IncludesEntityPrefix()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetTodos();
            public record GetTodo(string TodoId);
            public record CreateTodo(string Name);
            public record UpdateTodo(string TodoId, string Name);
            public record DeleteTodo(string TodoId);
            public record CompleteTodo(string TodoId);

            public class TodoHandler
            {
                public string[] Handle(GetTodos query) => [];
                public string Handle(GetTodo query) => "todo";
                public string Handle(CreateTodo command) => "created";
                public string Handle(UpdateTodo command) => "updated";
                public void Handle(DeleteTodo command) { }
                public Result Handle(CompleteTodo command) => Result.Success();
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/todos");
        AssertEndpoint(endpointSource, "GET", "/api/todos/{todoId}");
        AssertEndpoint(endpointSource, "POST", "/api/todos");
        AssertEndpoint(endpointSource, "PUT", "/api/todos/{todoId}");
        AssertEndpoint(endpointSource, "DELETE", "/api/todos/{todoId}");
        // Action verb should include the entity prefix
        AssertEndpoint(endpointSource, "POST", "/api/todos/{todoId}/complete");
    }

    [Fact]
    public void ActionVerb_SeparateHandler_IncludesEntityPrefix()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetTodos();
            public record GetTodo(string TodoId);
            public record CreateTodo(string Name);
            public record UpdateTodo(string TodoId, string Name);
            public record DeleteTodo(string TodoId);
            public record CompleteTodo(string TodoId);

            public class TodoHandler
            {
                public string[] Handle(GetTodos query) => [];
                public string Handle(GetTodo query) => "todo";
                public string Handle(CreateTodo command) => "created";
                public string Handle(UpdateTodo command) => "updated";
                public void Handle(DeleteTodo command) { }
            }

            public class CompleteTodoHandler
            {
                public Result Handle(CompleteTodo command) => Result.Success();
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/todos");
        AssertEndpoint(endpointSource, "GET", "/api/todos/{todoId}");
        AssertEndpoint(endpointSource, "POST", "/api/todos");
        AssertEndpoint(endpointSource, "PUT", "/api/todos/{todoId}");
        AssertEndpoint(endpointSource, "DELETE", "/api/todos/{todoId}");
        // Action verb in separate handler should still include the entity prefix
        AssertEndpoint(endpointSource, "POST", "/api/todos/{todoId}/complete");
    }

    [Fact]
    public void ActionVerb_StandaloneHandler_IncludesEntityPrefix()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record CompleteTodo(string TodoId);

            public class TodoHandler
            {
                public Result Handle(CompleteTodo command) => Result.Success();
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Even standalone, the entity prefix should be derived from the message name
        AssertEndpoint(endpointSource, "POST", "/api/todos/{todoId}/complete");
    }

    [Fact]
    public void ActionVerb_WithExplicitGroup_IncludesEntityPrefix()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetTodos();
            public record GetTodo(string TodoId);
            public record CreateTodo(string Name);
            public record UpdateTodo(string TodoId, string Name);
            public record DeleteTodo(string TodoId);
            public record CompleteTodo(string TodoId);

            [HandlerEndpointGroup("Todos")]
            public class TodoHandler
            {
                public string[] Handle(GetTodos query) => [];
                public string Handle(GetTodo query) => "todo";
                public string Handle(CreateTodo command) => "created";
                public string Handle(UpdateTodo command) => "updated";
                public void Handle(DeleteTodo command) { }
                public Result Handle(CompleteTodo command) => Result.Success();
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/todos");
        AssertEndpoint(endpointSource, "GET", "/api/todos/{todoId}");
        AssertEndpoint(endpointSource, "POST", "/api/todos");
        AssertEndpoint(endpointSource, "PUT", "/api/todos/{todoId}");
        AssertEndpoint(endpointSource, "DELETE", "/api/todos/{todoId}");
        AssertEndpoint(endpointSource, "POST", "/api/todos/{todoId}/complete");
    }

    [Fact]
    public void SeparateHandlers_CrudMessages_GeneratesCorrectRoutes()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetTodo(string TodoId);
            public record GetAllTodos();
            public record CreateTodo(string Name);
            public record UpdateTodo(string TodoId, string Name);
            public record DeleteTodo(string TodoId);

            public class GetTodoHandler
            {
                public string Handle(GetTodo query) => "todo";
            }

            public class GetAllTodosHandler
            {
                public string[] Handle(GetAllTodos query) => [];
            }

            public class CreateTodoHandler
            {
                public string Handle(CreateTodo command) => "created";
            }

            public class UpdateTodoHandler
            {
                public string Handle(UpdateTodo command) => "updated";
            }

            public class DeleteTodoHandler
            {
                public void Handle(DeleteTodo command) { }
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/todos/{todoId}");
        AssertEndpoint(endpointSource, "GET", "/api/todos");
        AssertEndpoint(endpointSource, "POST", "/api/todos");
        AssertEndpoint(endpointSource, "PUT", "/api/todos/{todoId}");
        AssertEndpoint(endpointSource, "DELETE", "/api/todos/{todoId}");

        AssertNoRouteContains(endpointSource, "/create");
        AssertNoRouteContains(endpointSource, "/update");
        AssertNoRouteContains(endpointSource, "/delete");
    }

    [Theory]
    [InlineData("Get", "Order", "string OrderId", "GET")]
    [InlineData("Find", "Order", "string OrderId", "GET")]
    [InlineData("Search", "Orders", "", "GET")]
    [InlineData("List", "Orders", "", "GET")]
    [InlineData("Query", "Orders", "", "GET")]
    [InlineData("Create", "Order", "string Name", "POST")]
    [InlineData("Add", "Order", "string Name", "POST")]
    [InlineData("New", "Order", "string Name", "POST")]
    [InlineData("Update", "Order", "string OrderId, string Name", "PUT")]
    [InlineData("Edit", "Order", "string OrderId, string Name", "PUT")]
    [InlineData("Modify", "Order", "string OrderId, string Name", "PUT")]
    [InlineData("Change", "Order", "string OrderId, string Name", "PUT")]
    [InlineData("Set", "Order", "string OrderId, string Name", "PUT")]
    [InlineData("Delete", "Order", "string OrderId", "DELETE")]
    [InlineData("Remove", "Order", "string OrderId", "DELETE")]
    [InlineData("Patch", "Order", "string OrderId, string Name", "PATCH")]
    public void VerbPrefix_InfersCorrectHttpMethod(string verb, string entity, string properties, string expectedMethod)
    {
        var source = $$"""
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record {{verb}}{{entity}}({{properties}});

            public class {{entity}}Handler
            {
                public string Handle({{verb}}{{entity}} msg) => "ok";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        var endpoints = ParseEndpointLogEntries(endpointSource);
        Assert.True(endpoints.Any(e => e.Method == expectedMethod),
            $"Expected HTTP method {expectedMethod} not found.\nFound: {string.Join(", ", endpoints.Select(e => e.Method))}\n\nGenerated source:\n{endpointSource}");

        // CRUD verb should not appear as route suffix
        AssertNoRouteContains(endpointSource, $"/{verb.ToLowerInvariant()}");
    }

    [Theory]
    [InlineData("Complete", "Order", "string OrderId")]
    [InlineData("Approve", "Order", "string OrderId")]
    [InlineData("Cancel", "Order", "string OrderId")]
    [InlineData("Submit", "Order", "string OrderId")]
    [InlineData("Process", "Order", "string OrderId")]
    [InlineData("Publish", "Order", "string OrderId")]
    [InlineData("Archive", "Order", "string OrderId")]
    [InlineData("Restore", "Order", "string OrderId")]
    [InlineData("Enable", "Feature", "string FeatureId")]
    [InlineData("Disable", "Feature", "string FeatureId")]
    [InlineData("Lock", "Account", "string AccountId")]
    [InlineData("Unlock", "Account", "string AccountId")]
    [InlineData("Activate", "Account", "string AccountId")]
    [InlineData("Deactivate", "Account", "string AccountId")]
    [InlineData("Sync", "Data", "string DataId")]
    [InlineData("Refresh", "Token", "string TokenId")]
    [InlineData("Trigger", "Build", "string BuildId")]
    [InlineData("Execute", "Task", "string TaskId")]
    [InlineData("Run", "Job", "string JobId")]
    [InlineData("Send", "Notification", "string NotificationId")]
    [InlineData("Notify", "User", "string UserId")]
    [InlineData("Export", "Orders", "")]
    [InlineData("Import", "Orders", "")]
    [InlineData("Download", "Report", "string ReportId")]
    [InlineData("Upload", "Document", "string DocumentId")]
    public void ActionVerb_GeneratesPostWithActionSuffix(string verb, string entity, string properties)
    {
        var propsPart = string.IsNullOrEmpty(properties) ? "" : properties;
        var source = $$"""
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record {{verb}}{{entity}}({{propsPart}});

            public class {{entity}}Handler
            {
                public Result Handle({{verb}}{{entity}} msg) => Result.Success();
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Action verbs always generate POST with action verb as route suffix
        var kebabVerb = ToKebabCase(verb);
        var endpoints = ParseEndpointLogEntries(endpointSource);
        Assert.True(endpoints.Any(e => e.Method == "POST" && e.Route.Contains($"/{kebabVerb}")),
            $"Expected POST endpoint with /{kebabVerb} in route not found.\nFound: {string.Join(", ", endpoints.Select(e => $"{e.Method} {e.Route}"))}\n\nGenerated source:\n{endpointSource}");
    }

    [Fact]
    public void AllPrefix_StrippedFromEntityName()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetAllProducts();

            public class ProductHandler
            {
                public string[] Handle(GetAllProducts query) => [];
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/products");
        AssertNoRouteContains(endpointSource, "all-products");
    }

    [Fact]
    public void EntitySuffix_ById_StrippedFromEntityName()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetOrderById(string Id);

            public class OrderHandler
            {
                public string Handle(GetOrderById query) => "order";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/orders/{id}");
        AssertNoRouteContains(endpointSource, "by-id");
    }

    [Fact]
    public void EntitySuffix_Details_StrippedFromEntityName()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetProductDetails(string ProductId);

            public class ProductHandler
            {
                public string Handle(GetProductDetails query) => "details";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/products/{productId}");
        AssertNoRouteContains(endpointSource, "details");
    }

    [Fact]
    public void EntitySuffix_Detail_StrippedFromEntityName()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetProductDetail(string ProductId);

            public class ProductHandler
            {
                public string Handle(GetProductDetail query) => "detail";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/products/{productId}");
        AssertNoRouteContains(endpointSource, "detail");
    }

    [Fact]
    public void EntitySuffix_Summary_StrippedFromEntityName()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetOrderSummary(string OrderId);

            public class OrderHandler
            {
                public string Handle(GetOrderSummary query) => "summary";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/orders/{orderId}");
        AssertNoRouteContains(endpointSource, "summary");
    }

    [Fact]
    public void EntitySuffix_Stream_StrippedFromEntityName()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetEventStream();

            public class EventHandler
            {
                public string Handle(GetEventStream query) => "stream";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/events");
        AssertNoRouteContains(endpointSource, "stream");
    }

    [Fact]
    public void EntitySuffix_Paginated_StrippedFromEntityName()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetUsersPaginated(int Page, int PageSize);

            public class UserHandler
            {
                public string[] Handle(GetUsersPaginated query) => [];
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/users");
        AssertNoRouteContains(endpointSource, "paginated");
    }

    [Fact]
    public void EntitySuffix_Paged_StrippedFromEntityName()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetOrdersPaged(int Page);

            public class OrderHandler
            {
                public string[] Handle(GetOrdersPaged query) => [];
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/orders");
        AssertNoRouteContains(endpointSource, "paged");
    }

    [Fact]
    public void EntitySuffix_List_StrippedFromEntityName()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetTodoList();

            public class TodoHandler
            {
                public string[] Handle(GetTodoList query) => [];
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/todos");
        AssertNoRouteContains(endpointSource, "todo-list");
    }

    [Fact]
    public void WithFeature_StrippedFromEntityName()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetOrdersWithFiltering(string? Status);

            public class OrderHandler
            {
                public string[] Handle(GetOrdersWithFiltering query) => [];
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/orders");
        AssertNoRouteContains(endpointSource, "filtering");
        AssertNoRouteContains(endpointSource, "with-filtering");
    }

    [Fact]
    public void CountSuffix_BecomesRouteSegment()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetProductCount();

            public class ProductHandler
            {
                public int Handle(GetProductCount query) => 42;
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/products/count");
    }

    [Fact]
    public void ForEntity_BecomesRouteSegment()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetInvoicesForCustomer(string CustomerId);

            public class InvoiceHandler
            {
                public string[] Handle(GetInvoicesForCustomer query) => [];
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/invoices/for-customer/{customerId}");
    }

    [Fact]
    public void FromEntity_BecomesRouteSegment()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetShipmentsFromWarehouse(string WarehouseId);

            public class ShipmentHandler
            {
                public string[] Handle(GetShipmentsFromWarehouse query) => [];
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/shipments/from-warehouse/{warehouseId}");
    }

    [Fact]
    public void ByProperty_BecomesRouteSegment()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetUserByEmail(string Email);

            public class UserHandler
            {
                public string Handle(GetUserByEmail query) => "user";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Email is not an Id property, so it becomes a query param, not a route param
        AssertEndpoint(endpointSource, "GET", "/api/users/by-email");
        Assert.Contains("FromQuery", endpointSource);
    }

    [Fact]
    public void Pluralization_RegularNoun()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetWidget(string WidgetId);

            public class WidgetHandler
            {
                public string Handle(GetWidget query) => "widget";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/widgets/{widgetId}");
    }

    [Fact]
    public void Pluralization_ConsonantY()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetCategory(string CategoryId);

            public class CategoryHandler
            {
                public string Handle(GetCategory query) => "category";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/categories/{categoryId}");
        AssertNoRouteContains(endpointSource, "/categorys");
    }

    [Fact]
    public void Pluralization_VowelY()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetSurvey(string SurveyId);

            public class SurveyHandler
            {
                public string Handle(GetSurvey query) => "survey";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/surveys/{surveyId}");
        AssertNoRouteContains(endpointSource, "/surveies");
    }

    [Fact]
    public void Pluralization_EndsWithX()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetBox(string BoxId);

            public class BoxHandler
            {
                public string Handle(GetBox query) => "box";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/boxes/{boxId}");
    }

    [Fact]
    public void Pluralization_EndsWithCh()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetBatch(string BatchId);

            public class BatchHandler
            {
                public string Handle(GetBatch query) => "batch";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/batches/{batchId}");
    }

    [Fact]
    public void Pluralization_EndsWithSh()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetWish(string WishId);

            public class WishHandler
            {
                public string Handle(GetWish query) => "wish";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/wishes/{wishId}");
    }

    [Fact]
    public void Pluralization_AlreadyPlural()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetAllSettings();

            public class SettingsHandler
            {
                public string[] Handle(GetAllSettings query) => [];
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/settings");
        AssertNoRouteContains(endpointSource, "/settingss");
    }

    [Fact]
    public void Pluralization_Uncountable_Health()
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

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/health");
        AssertNoRouteContains(endpointSource, "/healths");
    }

    [Fact]
    public void Pluralization_Uncountable_Status()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetStatus();

            public class StatusHandler
            {
                public string Handle(GetStatus query) => "ok";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/status");
        AssertNoRouteContains(endpointSource, "/statuss");
        AssertNoRouteContains(endpointSource, "/statuses");
    }

    [Fact]
    public void Pluralization_Uncountable_Data()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetData();

            public class DataHandler
            {
                public string Handle(GetData query) => "data";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/data");
        AssertNoRouteContains(endpointSource, "/datas");
    }

    [Fact]
    public void Pluralization_IrregularNoun_Person()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetPerson(string PersonId);

            public class PersonHandler
            {
                public string Handle(GetPerson query) => "person";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/people/{personId}");
        AssertNoRouteContains(endpointSource, "/persons");
    }

    [Fact]
    public void IdProperty_BecomesRouteParam()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetInvoice(string InvoiceId);

            public class InvoiceHandler
            {
                public string Handle(GetInvoice query) => "invoice";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/invoices/{invoiceId}");
    }

    [Fact]
    public void MultipleIdProperties_AllBecomeRouteParams()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetOrderItem(string OrderId, string ItemId);

            public class OrderItemHandler
            {
                public string Handle(GetOrderItem query) => "item";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/order-items/{orderId}/{itemId}");
    }

    [Fact]
    public void NonIdProperty_Get_BecomesQueryParam()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record SearchProducts(string? Category, int? MaxPrice);

            public class ProductHandler
            {
                public string[] Handle(SearchProducts query) => [];
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/products");
        // Non-Id properties on GET should NOT be route params
        AssertNoRouteContains(endpointSource, "{category}");
        AssertNoRouteContains(endpointSource, "{maxPrice}");
        // Should not use FromBody for GET
        Assert.DoesNotContain("FromBody", endpointSource);
    }

    [Fact]
    public void NonIdProperty_Post_BecomesBody()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record CreateProduct(string Name, string Description, decimal Price);

            public class ProductHandler
            {
                public string Handle(CreateProduct command) => "created";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "POST", "/api/products");
        // Non-Id properties on POST → body binding
        Assert.Contains("FromBody", endpointSource);
        // Properties should NOT be route params
        AssertNoRouteContains(endpointSource, "{name}");
        AssertNoRouteContains(endpointSource, "{description}");
        AssertNoRouteContains(endpointSource, "{price}");
    }

    [Fact]
    public void GenericIdProperty_BecomesRouteParam()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record DeleteWidget(string Id);

            public class WidgetHandler
            {
                public void Handle(DeleteWidget command) { }
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "DELETE", "/api/widgets/{id}");
    }

    [Fact]
    public void MultiWordEntity_ProducesKebabCaseRoute()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetShoppingCartItem(string ShoppingCartItemId);

            public class ShoppingCartItemHandler
            {
                public string Handle(GetShoppingCartItem query) => "item";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/shopping-cart-items/{shoppingCartItemId}");
    }

    [Fact]
    public void MultiWordActionVerb_ProducesKebabCaseRoute()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record CompleteSomethingImportant(string SomethingImportantId);

            public class SomethingImportantHandler
            {
                public Result Handle(CompleteSomethingImportant msg) => Result.Success();
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        var endpoints = ParseEndpointLogEntries(endpointSource);
        Assert.True(endpoints.Any(e => e.Method == "POST" && e.Route.Contains("/complete") && e.Route.Contains("{somethingImportantId}")),
            $"Expected POST endpoint with /complete and {{somethingImportantId}} not found.\nFound: {string.Join(", ", endpoints.Select(e => $"{e.Method} {e.Route}"))}");
    }

    [Fact]
    public void ExplicitGroup_UsesGroupNameForRoutePrefix()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All, EndpointRoutePrefix = "api")]

            public record GetTodo(string TodoId);
            public record CreateTodo(string Name);

            [HandlerEndpointGroup("Todos")]
            public class TodoHandler
            {
                public string Handle(GetTodo query) => "todo";
                public string Handle(CreateTodo command) => "created";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/todos/{todoId}");
        AssertEndpoint(endpointSource, "POST", "/api/todos");
        Assert.Contains(".WithTags(\"Todos\")", endpointSource);
    }

    [Fact]
    public void NoExplicitGroup_DerivesGroupFromMessages()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetProduct(string ProductId);
            public record CreateProduct(string Name);

            public class ProductHandler
            {
                public string Handle(GetProduct query) => "product";
                public string Handle(CreateProduct command) => "created";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", "/api/products/{productId}");
        AssertEndpoint(endpointSource, "POST", "/api/products");
    }

    // ── Single-word message routing ──────────────────────────────────────

    [Fact]
    public void SingleWord_UsesHandlerPrefixAndMessageName()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record Login(string Username, string Password);
            public record Logout;

            public class AuthHandler
            {
                public string Handle(Login command) => "ok";
                public void Handle(Logout command) { }
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Single-word messages should use handler prefix + message name
        AssertEndpoint(endpointSource, "POST", "/api/auth/login");
        AssertEndpoint(endpointSource, "POST", "/api/auth/logout");
        // Should NOT pluralize single-word messages
        AssertNoRouteContains(endpointSource, "/logouts");
        AssertNoRouteContains(endpointSource, "/logins");
    }

    [Fact]
    public void SingleWord_WithGroup_UsesMessageNameOnly()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record Login(string Username, string Password);
            public record Logout;

            [HandlerEndpointGroup("Auth")]
            public class AuthHandler
            {
                public string Handle(Login command) => "ok";
                public void Handle(Logout command) { }
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // With explicit group, should use group prefix + message name
        AssertEndpoint(endpointSource, "POST", "/api/auth/login");
        AssertEndpoint(endpointSource, "POST", "/api/auth/logout");
    }

    [Fact]
    public void SingleWord_MatchesHandlerPrefix_NoDuplication()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record Ping;

            public class PingHandler
            {
                public string Handle(Ping command) => "pong";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // When handler prefix matches message name, no duplication
        AssertEndpoint(endpointSource, "POST", "/api/ping");
        AssertNoRouteContains(endpointSource, "/ping/ping");
    }

    [Fact]
    public void MultiWord_StillPluralizes()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetWidget(string Id);
            public record CreateWidget(string Name);

            public class WidgetHandler
            {
                public string Handle(GetWidget query) => "widget";
                public string Handle(CreateWidget command) => "created";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Multi-word messages with verb prefixes should still use normal CRUD conventions
        AssertEndpoint(endpointSource, "GET", "/api/widgets/{id}");
        AssertEndpoint(endpointSource, "POST", "/api/widgets");
    }

    [Fact]
    public void ExplicitRoute_AutoDerivesGroupFromHandler()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetCurrentUser;

            public class AuthHandler
            {
                [HandlerEndpoint(Route = "me")]
                public string Handle(GetCurrentUser query) => "user";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Explicit relative route + no [HandlerEndpointGroup] should auto-derive group from handler
        AssertEndpoint(endpointSource, "GET", "/api/auth/me");
        AssertNoRouteContains(endpointSource, "/api/me");
    }

    [Fact]
    public void ExplicitAbsoluteRoute_DoesNotAutoDerive()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record HealthCheck;

            public class SystemHandler
            {
                [HandlerEndpoint(Route = "/health")]
                public string Handle(HealthCheck query) => "ok";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Absolute routes (leading /) bypass all prefixes — no auto-derive
        AssertEndpoint(endpointSource, "POST", "/health");
        AssertNoRouteContains(endpointSource, "/system/health");
    }

    [Fact]
    public void ExplicitRoute_WithExplicitGroup_UsesGroupNotHandler()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetProfile;

            [HandlerEndpointGroup("Account")]
            public class UserHandler
            {
                [HandlerEndpoint(Route = "profile")]
                public string Handle(GetProfile query) => "profile";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Explicit [HandlerEndpointGroup] should take precedence over auto-derive
        AssertEndpoint(endpointSource, "GET", "/api/account/profile");
        AssertNoRouteContains(endpointSource, "/user/profile");
    }

    [Fact]
    public void CrudRoutes_NoAutoDerive_StillPluralized()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record GetOrder(string OrderId);
            public record CreateOrder(string Name);

            public class OrderHandler
            {
                public string Handle(GetOrder query) => "order";
                public string Handle(CreateOrder command) => "created";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Standard CRUD routes without explicit Route should NOT auto-derive group
        AssertEndpoint(endpointSource, "GET", "/api/orders/{orderId}");
        AssertEndpoint(endpointSource, "POST", "/api/orders");
        AssertNoRouteContains(endpointSource, "/order/");
    }

    // ── Theory tests: Naming Convention Matrix ──────────────────────────────────
    // These lock in the expected routes so convention changes are caught immediately.
    // To guarantee route stability across library upgrades, use [HandlerEndpoint] or [HandlerEndpointGroup].

    [Theory]
    [MemberData(nameof(EndpointNamingConventionData.SingleHandlerMessageOnlyCases), MemberType = typeof(EndpointNamingConventionData))]
    public void NamingConvention_SingleHandler_MatchingName_MessageOnlyRoute(
        string handlerClassName, string messageName, string messageProperties,
        string expectedHttpMethod, string expectedRoute)
    {
        var propsPart = string.IsNullOrEmpty(messageProperties) ? "" : messageProperties;
        var source = $$"""
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record {{messageName}}({{propsPart}});

            public class {{handlerClassName}}
            {
                public string Handle({{messageName}} msg) => "ok";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, expectedHttpMethod, expectedRoute);
    }

    [Theory]
    [MemberData(nameof(EndpointNamingConventionData.MultiHandlerGroupCases), MemberType = typeof(EndpointNamingConventionData))]
    public void NamingConvention_MultiHandler_AutoDerivesGroup(
        string handlerClassName, string messagesSpec, string expectedGroupTag,
        string expectedRoutesSpec)
    {
        // Parse messagesSpec: "GetOrder:string OrderId;CreateOrder:string Name"
        var messages = messagesSpec.Split(';');
        var recordDefs = new System.Text.StringBuilder();
        var handleMethods = new System.Text.StringBuilder();

        foreach (var msg in messages)
        {
            var parts = msg.Split(':');
            var msgName = parts[0];
            var msgProps = parts.Length > 1 ? parts[1] : "";
            recordDefs.AppendLine($"public record {msgName}({msgProps});");
            handleMethods.AppendLine($"    public string Handle({msgName} msg) => \"ok\";");
        }

        var source = $$"""
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            {{recordDefs}}
            public class {{handlerClassName}}
            {
            {{handleMethods}}
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Verify group tag
        Assert.Contains($".WithTags(\"{expectedGroupTag}\")", endpointSource);

        // Verify each expected route
        var expectedRoutes = expectedRoutesSpec.Split(';');
        foreach (var expected in expectedRoutes)
        {
            var spaceIdx = expected.IndexOf(' ');
            var method = expected.Substring(0, spaceIdx);
            var route = expected.Substring(spaceIdx + 1);
            AssertEndpoint(endpointSource, method, route);
        }
    }

    [Theory]
    [MemberData(nameof(EndpointNamingConventionData.SingleHandlerNonMatchingNameCases), MemberType = typeof(EndpointNamingConventionData))]
    public void NamingConvention_SingleHandler_NonMatchingName_AutoDerivesGroup(
        string handlerClassName, string messageName, string messageProperties,
        string expectedHttpMethod, string expectedRoute, string expectedGroupTag)
    {
        var propsPart = string.IsNullOrEmpty(messageProperties) ? "" : messageProperties;

        var source = $$"""
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            public record {{messageName}}({{propsPart}});

            public class {{handlerClassName}}
            {
                public string Handle({{messageName}} msg) => "ok";
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, expectedHttpMethod, expectedRoute);
        Assert.Contains($".WithTags(\"{expectedGroupTag}\")", endpointSource);
    }

    [Theory]
    [MemberData(nameof(EndpointNamingConventionData.ExplicitOverrideCases), MemberType = typeof(EndpointNamingConventionData))]
    public void NamingConvention_ExplicitAttributes_OverrideConvention(
        string source, string expectedRoutesSpec)
    {
        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        var expectedRoutes = expectedRoutesSpec.Split(';');
        foreach (var expected in expectedRoutes)
        {
            var spaceIdx = expected.IndexOf(' ');
            var method = expected.Substring(0, spaceIdx);
            var route = expected.Substring(spaceIdx + 1);
            AssertEndpoint(endpointSource, method, route);
        }
    }

    [Theory]
    [MemberData(nameof(EndpointNamingConventionData.GlobalPrefixCases), MemberType = typeof(EndpointNamingConventionData))]
    public void NamingConvention_GlobalPrefix_CombinesWithConvention(
        string routePrefix, string handlerClassName, string messageName,
        string messageProperties, string expectedRoute)
    {
        var propsPart = string.IsNullOrEmpty(messageProperties) ? "" : messageProperties;
        // Determine handler method count: if class name minus suffix equals message name → single handler
        // For the global prefix test, we use single-handler format for matching names, multi-handler for non-matching
        var classPrefix = handlerClassName.Replace("Handler", "");
        bool isSingleHandler = classPrefix == messageName;

        string source;
        if (isSingleHandler)
        {
            source = $$"""
                using Foundatio.Mediator;

                [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All, EndpointRoutePrefix = "{{routePrefix}}")]

                public record {{messageName}}({{propsPart}});

                public class {{handlerClassName}}
                {
                    public string Handle({{messageName}} msg) => "ok";
                }
                """;
        }
        else
        {
            // Non-matching name: make a multi-handler to trigger group mode, or single non-matching
            source = $$"""
                using Foundatio.Mediator;

                [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All, EndpointRoutePrefix = "{{routePrefix}}")]

                public record {{messageName}}({{propsPart}});

                public class {{handlerClassName}}
                {
                    public string Handle({{messageName}} msg) => "ok";
                }
                """;
        }

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "GET", expectedRoute);
    }

    [Theory]
    [MemberData(nameof(EndpointNamingConventionData.SingleWordMessageCases), MemberType = typeof(EndpointNamingConventionData))]
    public void NamingConvention_SingleWordMessage_UsesHandlerPrefix(
        string handlerClassName, string messageName, string messageProperties,
        string expectedRoute)
    {
        var propsPart = string.IsNullOrEmpty(messageProperties) ? "" : messageProperties;

        // Single-word messages need a second handler method to trigger group mode (unless handler matches)
        var classPrefix = handlerClassName.Replace("Handler", "");
        var classMatch = classPrefix == messageName;

        string source;
        if (classMatch)
        {
            // PingHandler + Ping: single-handler, message-only mode, but bare action path applies
            source = $$"""
                using Foundatio.Mediator;

                [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

                public record {{messageName}}({{propsPart}});

                public class {{handlerClassName}}
                {
                    public string Handle({{messageName}} msg) => "ok";
                }
                """;
        }
        else
        {
            // AuthHandler + Login/Logout: these are bare actions; need both to show grouping
            // But we test them individually as single-handler non-matching cases.
            // The AuthHandler with Login will get auto-grouped, and the bare action
            // logic in GenerateRoute handles the handler prefix grouping.
            source = $$"""
                using Foundatio.Mediator;

                [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

                public record {{messageName}}({{propsPart}});

                public class {{handlerClassName}}
                {
                    public string Handle({{messageName}} msg) => "ok";
                }
                """;
        }

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        AssertEndpoint(endpointSource, "POST", expectedRoute);
    }

    [Fact]
    public void NamingConvention_MixedClasses_SingleAndMultiHandler_BothWork()
    {
        var source = """
            using Foundatio.Mediator;

            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]

            // Single-handler class (message-only mode)
            public record GetProduct(string ProductId);
            public class GetProductHandler
            {
                public string Handle(GetProduct query) => "product";
            }

            // Multi-handler class (group mode)
            public record GetOrder(string OrderId);
            public record CreateOrder(string Name);
            public record DeleteOrder(string OrderId);
            public class OrderHandler
            {
                public string Handle(GetOrder query) => "order";
                public string Handle(CreateOrder command) => "created";
                public void Handle(DeleteOrder command) { }
            }
            """;

        var endpointSource = GenerateEndpointSource(source);
        if (endpointSource is null) return;

        // Single-handler: route from message only
        AssertEndpoint(endpointSource, "GET", "/api/products/{productId}");

        // Multi-handler: auto-grouped under /orders
        AssertEndpoint(endpointSource, "GET", "/api/orders/{orderId}");
        AssertEndpoint(endpointSource, "POST", "/api/orders");
        AssertEndpoint(endpointSource, "DELETE", "/api/orders/{orderId}");

        // Multi-handler class should have group tag
        Assert.Contains(".WithTags(\"Orders\")", endpointSource);
    }
}
