using System.ComponentModel.DataAnnotations;

namespace ConsoleSample.Messages;

#region Simple
public record Ping(string Text);
public record GetGreeting(string Name);
#endregion

// Order CRUD messages
public record CreateOrder(
    [Required(ErrorMessage = "Customer ID is required")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Customer ID must be between 3 and 50 characters")]
    string CustomerId,

    [Required(ErrorMessage = "Amount is required")]
    [Range(0.01, 1000000, ErrorMessage = "Amount must be between $0.01 and $1,000,000")]
    decimal Amount,

    [Required(ErrorMessage = "Description is required")]
    [StringLength(200, MinimumLength = 5, ErrorMessage = "Description must be between 5 and 200 characters")]
    string Description) : IValidatable, IRetryableMessage;
public record GetOrder(string OrderId);
public record UpdateOrder(string OrderId, decimal? Amount, string? Description);
public record DeleteOrder(string OrderId);

// Event messages (for publish pattern)
public record OrderCreated(string OrderId, string CustomerId, decimal Amount, DateTime CreatedAt);
public record OrderUpdated(string OrderId, decimal Amount, DateTime UpdatedAt);
public record OrderDeleted(string OrderId, DateTime DeletedAt);

// Order model
public record Order(string Id, string CustomerId, decimal Amount, string Description, DateTime CreatedAt, DateTime? UpdatedAt = null);

// Counter stream request
public record CounterStreamRequest { }

public interface IValidatable { }
