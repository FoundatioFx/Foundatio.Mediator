using Foundatio.Mediator;
using Orders.Module.Messages;
using Products.Module.Messages;

namespace WebApp.Api;

public static class SearchApi
{
    public static void MapSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/search", async (string? q, IMediator mediator) =>
        {
            var ordersTask = mediator.InvokeAsync(new GetOrders());
            var productsTask = mediator.InvokeAsync(new GetProducts());

            await Task.WhenAll(ordersTask.AsTask(), productsTask.AsTask());

            var orders = ordersTask.Result;
            var products = productsTask.Result;

            var query = q?.ToLowerInvariant() ?? "";

            var matchingOrders = orders.IsSuccess
                ? orders.Value?.Where(o =>
                    o.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    o.CustomerId.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList() ?? []
                : [];

            var matchingProducts = products.IsSuccess
                ? products.Value?.Where(p =>
                    p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    p.Description.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList() ?? []
                : [];

            return Results.Ok(new SearchResponse(
                Query: q,
                Orders: matchingOrders,
                Products: matchingProducts,
                TotalResults: matchingOrders.Count + matchingProducts.Count
            ));
        })
        .WithName("Search")
        .WithSummary("Search across orders and products");
    }
}

public record SearchResponse(
    string? Query,
    List<Order> Orders,
    List<Product> Products,
    int TotalResults);
