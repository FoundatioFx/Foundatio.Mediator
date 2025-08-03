using MediatR;
using Foundatio.Mediator.Benchmarks.Services;

namespace Foundatio.Mediator.Benchmarks.Messages;

// Scenario 1: Simple command for InvokeAsync without response
public record PingCommand(string Id) : IRequest;

// Scenario 2: Query with return value for InvokeAsync<T>
public record GetOrder(int Id) : IRequest<Order>;

// Scenario 2b: Query with dependency injection
public record GetOrderWithDependencies(int Id) : IRequest<Order>;

// Scenario 3: Notification for PublishAsync with single handler
public record UserRegisteredEvent(string UserId, string Email) : MediatR.INotification;
