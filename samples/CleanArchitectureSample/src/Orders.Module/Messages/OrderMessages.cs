using System.ComponentModel.DataAnnotations;
using Common.Module.Middleware;
using Foundatio.Mediator;
using Orders.Module.Domain;

namespace Orders.Module.Messages;

// Commands
public record CreateOrder(
    [Required(ErrorMessage = "Customer ID is required")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "Customer ID must be between 3 and 50 characters")]
    string CustomerId,

    [Required(ErrorMessage = "Amount is required")]
    [Range(0.01, 1000000, ErrorMessage = "Amount must be between $0.01 and $1,000,000")]
    decimal Amount,

    [Required(ErrorMessage = "Description is required")]
    [StringLength(200, MinimumLength = 5, ErrorMessage = "Description must be between 5 and 200 characters")]
    string Description) : IValidatable, ICommand<Result<Order>>;

public record UpdateOrder(
    [Required] string OrderId,
    decimal? Amount,
    string? Description,
    OrderStatus? Status) : IValidatable, ICommand<Result<Order>>;

public record DeleteOrder([Required] string OrderId) : IValidatable, ICommand<Result>;

// Queries
public record GetOrder([Required] string OrderId) : IValidatable, IQuery<Result<Order>>;
public record GetOrders() : IQuery<Result<List<Order>>>;
