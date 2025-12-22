using Foundatio.Mediator;
using Orders.Module.Messages;
using Products.Module.Messages;

namespace WebApp.Api;

public static class DashboardApi
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/dashboard")
            .WithTags("Dashboard");

        group.MapGet("/", async (IMediator mediator) =>
        {
            var ordersTask = mediator.InvokeAsync<Result<List<Order>>>(new GetOrders());
            var productsTask = mediator.InvokeAsync<Result<List<Product>>>(new GetProducts());

            await Task.WhenAll(ordersTask.AsTask(), productsTask.AsTask());

            var orders = ordersTask.Result;
            var products = productsTask.Result;

            return Results.Ok(new DashboardResponse(
                TotalOrders: orders.IsSuccess ? orders.Value?.Count ?? 0 : 0,
                TotalProducts: products.IsSuccess ? products.Value?.Count ?? 0 : 0,
                TotalRevenue: orders.IsSuccess ? orders.Value?.Sum(o => o.Amount) ?? 0 : 0,
                RecentOrders: orders.IsSuccess ? orders.Value?.OrderByDescending(o => o.CreatedAt).Take(5).ToList() ?? [] : [],
                RecentProducts: products.IsSuccess ? products.Value?.OrderByDescending(p => p.CreatedAt).Take(5).ToList() ?? [] : []
            ));
        })
        .WithName("GetDashboard")
        .WithSummary("Get dashboard overview with orders and products summary");

        group.MapPost("/quick-order", async (QuickOrderRequest request, IMediator mediator) =>
        {
            var command = new CreateOrder(request.CustomerId, request.Amount, request.Description ?? "Quick order");
            var result = await mediator.InvokeAsync<Result<Order>>(command);

            return result.IsSuccess
                ? Results.Created($"/api/orders/{result.Value?.Id}", result.Value)
                : Results.BadRequest(new { error = result.Message });
        })
        .WithName("QuickOrder")
        .WithSummary("Create a quick order with minimal input");

        group.MapPost("/quick-product", async (QuickProductRequest request, IMediator mediator) =>
        {
            var command = new CreateProduct(request.Name, request.Description ?? $"Product: {request.Name}", request.Price);
            var result = await mediator.InvokeAsync<Result<Product>>(command);

            return result.IsSuccess
                ? Results.Created($"/api/products/{result.Value?.Id}", result.Value)
                : Results.BadRequest(new { error = result.Message });
        })
        .WithName("QuickProduct")
        .WithSummary("Create a quick product with minimal input");
    }
}

// Request/Response DTOs
public record QuickOrderRequest(string CustomerId, decimal Amount, string? Description = null);
public record QuickProductRequest(string Name, decimal Price, string? Description = null);

public record DashboardResponse(
    int TotalOrders,
    int TotalProducts,
    decimal TotalRevenue,
    List<Order> RecentOrders,
    List<Product> RecentProducts);
