namespace Foundatio.Mediator.Tests;

/// <summary>
/// Centralized test data for endpoint naming convention tests.
/// Each dataset is a MemberData source for xUnit Theory tests that validate
/// the endpoint route generation rules. Adding a case here locks in the expected route
/// so convention changes are caught immediately.
/// </summary>
public static class EndpointNamingConventionData
{
    // ── Mode 1: Message-Only (single-handler, class name matches message) ──────

    /// <summary>
    /// Cases where the handler class has exactly one method and the class name (minus Handler/Consumer)
    /// matches the message type name. These produce routes from the message name alone — no endpoint group.
    /// Columns: handlerClassName, messageName, messageProperties, expectedHttpMethod, expectedRoute
    /// </summary>
    public static TheoryData<string, string, string, string, string> SingleHandlerMessageOnlyCases => new()
    {
        // CRUD verbs
        { "GetOrderHandler", "GetOrder", "string OrderId", "GET", "/api/orders/{orderId}" },
        { "GetOrdersHandler", "GetOrders", "", "GET", "/api/orders" },
        { "CreateOrderHandler", "CreateOrder", "string Name", "POST", "/api/orders" },
        { "UpdateOrderHandler", "UpdateOrder", "string OrderId, string Name", "PUT", "/api/orders/{orderId}" },
        { "DeleteOrderHandler", "DeleteOrder", "string OrderId", "DELETE", "/api/orders/{orderId}" },
        { "PatchOrderHandler", "PatchOrder", "string OrderId, string Name", "PATCH", "/api/orders/{orderId}" },

        // Alternate CRUD verb prefixes
        { "FindOrderHandler", "FindOrder", "string OrderId", "GET", "/api/orders/{orderId}" },
        { "SearchOrdersHandler", "SearchOrders", "", "GET", "/api/orders" },
        { "ListOrdersHandler", "ListOrders", "", "GET", "/api/orders" },
        { "QueryOrdersHandler", "QueryOrders", "", "GET", "/api/orders" },
        { "AddOrderHandler", "AddOrder", "string Name", "POST", "/api/orders" },
        { "NewOrderHandler", "NewOrder", "string Name", "POST", "/api/orders" },
        { "EditOrderHandler", "EditOrder", "string OrderId, string Name", "PUT", "/api/orders/{orderId}" },
        { "ModifyOrderHandler", "ModifyOrder", "string OrderId, string Name", "PUT", "/api/orders/{orderId}" },
        { "RemoveOrderHandler", "RemoveOrder", "string OrderId", "DELETE", "/api/orders/{orderId}" },

        // Action verbs (single-handler, class name matches message)
        { "CompleteTodoHandler", "CompleteTodo", "string TodoId", "POST", "/api/todos/{todoId}/complete" },
        { "ArchiveOrderHandler", "ArchiveOrder", "string OrderId", "POST", "/api/orders/{orderId}/archive" },
        { "ExportOrdersHandler", "ExportOrders", "", "POST", "/api/orders/export" },

        // Entity suffix stripping
        { "GetProductDetailsHandler", "GetProductDetails", "string ProductId", "GET", "/api/products/{productId}" },
        { "GetProductDetailHandler", "GetProductDetail", "string ProductId", "GET", "/api/products/{productId}" },
        { "GetOrderSummaryHandler", "GetOrderSummary", "string OrderId", "GET", "/api/orders/{orderId}" },
        { "GetOrderByIdHandler", "GetOrderById", "string Id", "GET", "/api/orders/{id}" },
        { "GetAllTodosHandler", "GetAllTodos", "", "GET", "/api/todos" },
        { "GetUsersPaginatedHandler", "GetUsersPaginated", "int Page, int PageSize", "GET", "/api/users" },
        { "GetOrdersPagedHandler", "GetOrdersPaged", "int Page", "GET", "/api/orders" },
        { "GetTodoListHandler", "GetTodoList", "", "GET", "/api/todos" },
        { "GetEventStreamHandler", "GetEventStream", "", "GET", "/api/events" },

        // With<Feature> stripping
        { "GetOrdersWithFilteringHandler", "GetOrdersWithFiltering", "string? Status", "GET", "/api/orders" },

        // Lookup patterns that become route segments
        { "GetProductCountHandler", "GetProductCount", "", "GET", "/api/products/count" },
        { "GetUserByEmailHandler", "GetUserByEmail", "string Email", "GET", "/api/users/by-email" },
        { "GetInvoicesForCustomerHandler", "GetInvoicesForCustomer", "string CustomerId", "GET", "/api/invoices/for-customer/{customerId}" },
        { "GetShipmentsFromWarehouseHandler", "GetShipmentsFromWarehouse", "string WarehouseId", "GET", "/api/shipments/from-warehouse/{warehouseId}" },

        // Pluralization edge cases
        { "GetCategoryHandler", "GetCategory", "string CategoryId", "GET", "/api/categories/{categoryId}" },
        { "GetPersonHandler", "GetPerson", "string PersonId", "GET", "/api/people/{personId}" },
        { "GetHealthHandler", "GetHealth", "", "GET", "/api/health" },
        { "GetStatusHandler", "GetStatus", "", "GET", "/api/status" },
        { "GetSurveyHandler", "GetSurvey", "string SurveyId", "GET", "/api/surveys/{surveyId}" },
        { "GetBoxHandler", "GetBox", "string BoxId", "GET", "/api/boxes/{boxId}" },
        { "GetBatchHandler", "GetBatch", "string BatchId", "GET", "/api/batches/{batchId}" },
        { "GetWishHandler", "GetWish", "string WishId", "GET", "/api/wishes/{wishId}" },

        // Multi-word entities (kebab-case)
        { "GetShoppingCartItemHandler", "GetShoppingCartItem", "string ShoppingCartItemId", "GET", "/api/shopping-cart-items/{shoppingCartItemId}" },

        // Multiple route params
        { "GetOrderItemHandler", "GetOrderItem", "string OrderId, string ItemId", "GET", "/api/order-items/{orderId}/{itemId}" },

        // Generic Id property
        { "DeleteWidgetHandler", "DeleteWidget", "string Id", "DELETE", "/api/widgets/{id}" },
    };

    // ── Mode 2: Multi-Handler Group (class has 2+ methods) ─────────────────────

    /// <summary>
    /// Cases where the handler class has multiple methods so it auto-derives an endpoint group
    /// from the class name. Routes within the group don't repeat the entity.
    /// Columns: handlerClassName, messageNames (semicolon-separated "Name:Props"), expectedGroupTag, expectedRoutes (semicolon-separated "METHOD /route")
    /// </summary>
    public static TheoryData<string, string, string, string> MultiHandlerGroupCases => new()
    {
        // Standard CRUD in one class
        {
            "OrderHandler",
            "GetOrder:string OrderId;GetOrders:;CreateOrder:string Name;UpdateOrder:string OrderId, string Name;DeleteOrder:string OrderId",
            "Orders",
            "GET /api/orders/{orderId};GET /api/orders;POST /api/orders;PUT /api/orders/{orderId};DELETE /api/orders/{orderId}"
        },
        // Plural class name
        {
            "OrdersHandler",
            "GetOrder:string OrderId;CreateOrder:string Name;DeleteOrder:string OrderId",
            "Orders",
            "GET /api/orders/{orderId};POST /api/orders;DELETE /api/orders/{orderId}"
        },
        // CRUD + action verb (action verb entity matches group → no duplication)
        {
            "TodoHandler",
            "GetTodo:string TodoId;CreateTodo:string Name;CompleteTodo:string TodoId",
            "Todos",
            "GET /api/todos/{todoId};POST /api/todos;POST /api/todos/{todoId}/complete"
        },
        // Action verb entity matches group name
        {
            "OrdersHandler",
            "GetOrder:string OrderId;PromoteOrder:string OrderId",
            "Orders",
            "GET /api/orders/{orderId};POST /api/orders/{orderId}/promote"
        },
        // Multi-word class name
        {
            "ShoppingCartHandler",
            "GetShoppingCart:string ShoppingCartId;CreateShoppingCart:string Name",
            "ShoppingCarts",
            "GET /api/shopping-carts/{shoppingCartId};POST /api/shopping-carts"
        },
    };

    // ── Mode detection: single handler with non-matching name → group mode ─────

    /// <summary>
    /// Cases where a single-handler class name does NOT match the message name,
    /// so it falls into group mode despite having only 1 method.
    /// Columns: handlerClassName, messageName, messageProperties, expectedHttpMethod, expectedRoute, expectedGroupTag
    /// </summary>
    public static TheoryData<string, string, string, string, string, string> SingleHandlerNonMatchingNameCases => new()
    {
        { "OrderHandler", "GetOrder", "string OrderId", "GET", "/api/orders/{orderId}", "Orders" },
        { "TodoHandler", "CompleteTodo", "string TodoId", "POST", "/api/todos/{todoId}/complete", "Todos" },
        { "ProductHandler", "CreateProduct", "string Name", "POST", "/api/products", "Products" },
    };

    // ── Explicit attribute overrides ───────────────────────────────────────────

    /// <summary>
    /// Cases where explicit attributes override the auto-derived convention.
    /// Columns: source, expectedRoutes (semicolon-separated "METHOD /route")
    /// </summary>
    public static TheoryData<string, string> ExplicitOverrideCases => new()
    {
        // [HandlerEndpointGroup] overrides auto-derive
        {
            """
            using Foundatio.Mediator;
            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]
            public record GetOrder(string OrderId);
            public record CreateOrder(string Name);
            [HandlerEndpointGroup("MyOrders")]
            public class OrderHandler
            {
                public string Handle(GetOrder query) => "order";
                public string Handle(CreateOrder command) => "created";
            }
            """,
            "GET /api/my-orders/{orderId};POST /api/my-orders"
        },
        // [HandlerEndpointGroup] with custom RoutePrefix
        {
            """
            using Foundatio.Mediator;
            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]
            public record GetOrder(string OrderId);
            public record CreateOrder(string Name);
            [HandlerEndpointGroup("Orders", RoutePrefix = "v2/orders")]
            public class OrderHandler
            {
                public string Handle(GetOrder query) => "order";
                public string Handle(CreateOrder command) => "created";
            }
            """,
            "GET /api/v2/orders/{orderId};POST /api/v2/orders"
        },
        // [HandlerEndpoint(Route = "...")] on method within auto-grouped class
        {
            """
            using Foundatio.Mediator;
            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]
            public record GetOrder(string OrderId);
            public record GetCurrentOrder;
            public class OrderHandler
            {
                public string Handle(GetOrder query) => "order";
                [HandlerEndpoint(Route = "current")]
                public string Handle(GetCurrentOrder query) => "current";
            }
            """,
            "GET /api/orders/{orderId};GET /api/orders/current"
        },
        // Absolute route bypasses all prefixes
        {
            """
            using Foundatio.Mediator;
            [assembly: MediatorConfiguration(EndpointDiscovery = EndpointDiscovery.All)]
            public record HealthCheck;
            public class SystemHandler
            {
                [HandlerEndpoint(Route = "/health")]
                public string Handle(HealthCheck query) => "ok";
            }
            """,
            "POST /health"
        },
    };

    // ── Global prefix interaction ──────────────────────────────────────────────

    /// <summary>
    /// Cases verifying EndpointRoutePrefix interacts correctly with auto-derived groups.
    /// Columns: routePrefix, handlerClassName, messageName, messageProperties, expectedRoute
    /// </summary>
    public static TheoryData<string, string, string, string, string> GlobalPrefixCases => new()
    {
        { "api", "GetOrderHandler", "GetOrder", "string OrderId", "/api/orders/{orderId}" },
        { "", "GetOrderHandler", "GetOrder", "string OrderId", "/orders/{orderId}" },
        { "api/v2", "GetOrderHandler", "GetOrder", "string OrderId", "/api/v2/orders/{orderId}" },
        { "api", "OrderHandler", "GetOrder", "string OrderId", "/api/orders/{orderId}" },
        { "", "OrderHandler", "GetOrder", "string OrderId", "/orders/{orderId}" },
    };

    // ── Single-word bare action messages ───────────────────────────────────────

    /// <summary>
    /// Cases for single-word messages (no verb prefix) that become bare actions.
    /// The handler class prefix provides grouping context.
    /// Columns: handlerClassName, messageName, messageProperties, expectedRoute
    /// </summary>
    public static TheoryData<string, string, string, string> SingleWordMessageCases => new()
    {
        { "AuthHandler", "Login", "string Username, string Password", "/api/auth/login" },
        { "AuthHandler", "Logout", "", "/api/auth/logout" },
        { "PingHandler", "Ping", "", "/api/ping" },
    };
}
