using System.Text.Json.Serialization;

namespace Orders.Module.Domain;

/// <summary>
/// Represents an order in the system.
/// This is a domain entity - the core business object for the Orders bounded context.
/// </summary>
public record Order(
    string Id,
    string CustomerId,
    decimal Amount,
    string Description,
    OrderStatus Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt = null);

/// <summary>
/// Represents the lifecycle status of an order.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrderStatus
{
    Pending,
    Confirmed,
    Processing,
    Shipped,
    Delivered,
    Cancelled
}
