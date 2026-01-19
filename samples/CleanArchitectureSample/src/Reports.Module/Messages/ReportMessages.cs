using Foundatio.Mediator;
using Orders.Module.Domain;
using Products.Module.Domain;

namespace Reports.Module.Messages;

// Queries - Reports module aggregates data from other modules via the mediator
public record GetDashboardReport() : IQuery<Result<DashboardReport>>;
public record GetSalesReport(DateTime? StartDate, DateTime? EndDate) : IQuery<Result<SalesReport>>;
public record GetInventoryReport() : IQuery<Result<InventoryReport>>;
public record SearchCatalog(string SearchTerm) : IQuery<Result<CatalogSearchResult>>;

// Report DTOs - These are specific to the Reports module
public record DashboardReport(
    int TotalOrders,
    int TotalProducts,
    decimal TotalRevenue,
    int LowStockProductCount,
    IReadOnlyList<RecentOrder> RecentOrders,
    IReadOnlyList<TopProduct> TopProducts);

public record RecentOrder(
    string OrderId,
    string CustomerId,
    decimal Amount,
    OrderStatus Status,
    DateTime CreatedAt);

public record TopProduct(
    string ProductId,
    string Name,
    decimal Price,
    int StockQuantity,
    ProductStatus Status);

public record SalesReport(
    DateTime StartDate,
    DateTime EndDate,
    int OrderCount,
    decimal TotalRevenue,
    decimal AverageOrderValue,
    IReadOnlyList<DailySales> DailySales);

public record DailySales(
    DateTime Date,
    int OrderCount,
    decimal Revenue);

public record InventoryReport(
    int TotalProducts,
    int ActiveProducts,
    int OutOfStockProducts,
    int LowStockProducts,
    decimal TotalInventoryValue,
    IReadOnlyList<LowStockProduct> LowStockItems);

public record LowStockProduct(
    string ProductId,
    string Name,
    int StockQuantity,
    int ReorderThreshold);

public record CatalogSearchResult(
    string SearchTerm,
    IReadOnlyList<ProductSearchResult> Products,
    IReadOnlyList<OrderSearchResult> Orders);

public record ProductSearchResult(
    string ProductId,
    string Name,
    string Description,
    decimal Price,
    ProductStatus Status);

public record OrderSearchResult(
    string OrderId,
    string CustomerId,
    string Description,
    decimal Amount,
    OrderStatus Status);
