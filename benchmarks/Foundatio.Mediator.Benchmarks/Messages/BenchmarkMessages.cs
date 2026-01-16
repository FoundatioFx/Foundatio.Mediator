using Foundatio.Mediator.Benchmarks.Services;
using MediatorLib = Mediator;

namespace Foundatio.Mediator.Benchmarks.Messages;

// Scenario 1: Simple command for InvokeAsync without response
public record PingCommand(string Id) : MediatR.IRequest, MediatorLib.ICommand;

// Scenario 2: Query with return value for InvokeAsync<T>
public record GetOrder(int Id) : MediatR.IRequest<Order>, MediatorLib.IQuery<Order>;

// Scenario 4: FullQuery - Query with dependency injection
public record GetFullQuery(int Id) : MediatR.IRequest<Order>, MediatorLib.IQuery<Order>;

// Scenario 3: Notification for PublishAsync with multiple handlers
public record UserRegisteredEvent(string UserId, string Email) : MediatR.INotification, MediatorLib.INotification;

// Scenario 5: Cascading messages - command that returns result and triggers events
public record CreateOrder(int CustomerId, decimal Amount) : MediatR.IRequest<Order>, MediatorLib.IRequest<Order>;
public record OrderCreatedEvent(int OrderId, int CustomerId) : MediatR.INotification, MediatorLib.INotification;

// Scenario 6: Short-circuit / Cache-hit - tests middleware that returns early without calling handler
// Each library implements this with their idiomatic approach:
// - Foundatio: HandlerResult.ShortCircuit(value) in middleware
// - MediatR: IPipelineBehavior returns cached value directly
// - Wolverine: HandlerContinuation.Stop with cached value
// - MediatorNet: IPipelineBehavior returns cached value directly
// - MassTransit: Filter returns without calling next.Send()
public record GetCachedOrder(int Id) : MediatR.IRequest<Order>, MediatorLib.IQuery<Order>;
