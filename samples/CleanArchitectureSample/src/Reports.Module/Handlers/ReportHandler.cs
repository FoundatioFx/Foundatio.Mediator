using Foundatio.Mediator;
using Microsoft.Extensions.Logging;
using Orders.Module.Messages;
using Products.Module.Domain;
using Products.Module.Messages;
using Reports.Module.Messages;

namespace Reports.Module.Handlers;

/// <summary>
/// Handles report generation by aggregating data from other modules via the mediator.
/// This demonstrates Clean Architecture's cross-module communication pattern:
/// - Reports.Module knows NOTHING about how Orders or Products are stored
/// - All data is fetched via published queries through the mediator
/// - Loose coupling enables independent module evolution
/// </summary>
[HandlerCategory("Reports", RoutePrefix = "/api/reports")]
public class ReportHandler(IMediator mediator, ILogger<ReportHandler> logger)
{
    private const int LowStockThreshold = 10;

    /// <summary>
    /// Generates a dashboard report combining data from Orders and Products modules
    /// </summary>
    public async Task<Result<DashboardReport>> HandleAsync(GetDashboardReport query, CancellationToken cancellationToken)
    {
        logger.LogInformation("Generating dashboard report");

        var ordersResult = await mediator.InvokeAsync(new GetOrders(), cancellationToken);
        var productsResult = await mediator.InvokeAsync(new GetProducts(), cancellationToken);

        if (!ordersResult.IsSuccess)
            return Result.Error($"Failed to fetch orders: {ordersResult.Message}");

        if (!productsResult.IsSuccess)
            return Result.Error($"Failed to fetch products: {productsResult.Message}");

        var orders = ordersResult.Value ?? [];
        var products = productsResult.Value ?? [];

        var report = new DashboardReport(
            TotalOrders: orders.Count,
            TotalProducts: products.Count,
            TotalRevenue: orders.Sum(o => o.Amount),
            LowStockProductCount: products.Count(p => p.StockQuantity <= LowStockThreshold && p.Status == ProductStatus.Active),
            RecentOrders: orders
                .OrderByDescending(o => o.CreatedAt)
                .Take(5)
                .Select(o => new RecentOrder(o.Id, o.CustomerId, o.Amount, o.Status, o.CreatedAt))
                .ToList(),
            TopProducts: products
                .Where(p => p.Status == ProductStatus.Active)
                .OrderByDescending(p => p.Price)
                .Take(5)
                .Select(p => new TopProduct(p.Id, p.Name, p.Price, p.StockQuantity, p.Status))
                .ToList()
        );

        logger.LogInformation("Dashboard report generated: {TotalOrders} orders, {TotalProducts} products, {Revenue:C} revenue",
            report.TotalOrders, report.TotalProducts, report.TotalRevenue);

        return report;
    }

    /// <summary>
    /// Generates a sales report for a date range
    /// </summary>
    public async Task<Result<SalesReport>> HandleAsync(GetSalesReport query, CancellationToken cancellationToken)
    {
        var startDate = query.StartDate ?? DateTime.UtcNow.AddDays(-30);
        var endDate = query.EndDate ?? DateTime.UtcNow;

        logger.LogInformation("Generating sales report for {StartDate} to {EndDate}", startDate, endDate);

        var ordersResult = await mediator.InvokeAsync(new GetOrders(), cancellationToken);

        if (!ordersResult.IsSuccess)
            return Result.Error($"Failed to fetch orders: {ordersResult.Message}");

        var orders = (ordersResult.Value ?? [])
            .Where(o => o.CreatedAt >= startDate && o.CreatedAt <= endDate)
            .ToList();

        var dailySales = orders
            .GroupBy(o => o.CreatedAt.Date)
            .Select(g => new DailySales(g.Key, g.Count(), g.Sum(o => o.Amount)))
            .OrderBy(d => d.Date)
            .ToList();

        var report = new SalesReport(
            StartDate: startDate,
            EndDate: endDate,
            OrderCount: orders.Count,
            TotalRevenue: orders.Sum(o => o.Amount),
            AverageOrderValue: orders.Count > 0 ? orders.Average(o => o.Amount) : 0,
            DailySales: dailySales
        );

        return report;
    }

    /// <summary>
    /// Generates an inventory report from the Products module
    /// </summary>
    public async Task<Result<InventoryReport>> HandleAsync(GetInventoryReport query, CancellationToken cancellationToken)
    {
        logger.LogInformation("Generating inventory report");

        var productsResult = await mediator.InvokeAsync(new GetProducts(), cancellationToken);

        if (!productsResult.IsSuccess)
            return Result.Error($"Failed to fetch products: {productsResult.Message}");

        var products = productsResult.Value ?? [];

        var lowStockItems = products
            .Where(p => p.StockQuantity <= LowStockThreshold && p.Status != ProductStatus.Discontinued)
            .Select(p => new LowStockProduct(p.Id, p.Name, p.StockQuantity, LowStockThreshold))
            .ToList();

        var report = new InventoryReport(
            TotalProducts: products.Count,
            ActiveProducts: products.Count(p => p.Status == ProductStatus.Active),
            OutOfStockProducts: products.Count(p => p.Status == ProductStatus.OutOfStock),
            LowStockProducts: lowStockItems.Count,
            TotalInventoryValue: products.Sum(p => p.Price * p.StockQuantity),
            LowStockItems: lowStockItems
        );

        return report;
    }

    /// <summary>
    /// Searches across both Orders and Products modules
    /// </summary>
    public async Task<Result<CatalogSearchResult>> HandleAsync(SearchCatalog query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.SearchTerm))
            return Result.Invalid(new ValidationError("SearchTerm", "Search term is required"));

        logger.LogInformation("Searching catalog for: {SearchTerm}", query.SearchTerm);

        // Fetch data from both modules via the mediator
        var ordersResult = await mediator.InvokeAsync(new GetOrders(), cancellationToken);
        var productsResult = await mediator.InvokeAsync(new GetProducts(), cancellationToken);

        var searchTerm = query.SearchTerm.ToLowerInvariant();

        var matchingProducts = (productsResult.Value ?? [])
            .Where(p => p.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                        p.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .Select(p => new ProductSearchResult(p.Id, p.Name, p.Description, p.Price, p.Status))
            .ToList();

        var matchingOrders = (ordersResult.Value ?? [])
            .Where(o => o.Description.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                        o.CustomerId.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .Select(o => new OrderSearchResult(o.Id, o.CustomerId, o.Description, o.Amount, o.Status))
            .ToList();

        return new CatalogSearchResult(query.SearchTerm, matchingProducts, matchingOrders);
    }
}
