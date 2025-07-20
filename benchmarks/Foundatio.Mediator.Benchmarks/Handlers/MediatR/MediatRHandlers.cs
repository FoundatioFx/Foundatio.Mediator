using Foundatio.Mediator.Benchmarks.Messages;
using MediatR;

namespace Foundatio.Mediator.Benchmarks.Handlers.MediatR;

// MediatR handlers using their interface-based approach, named with different suffix so Foundatio won't pick them up
[FoundatioIgnore]
public class MediatRPingHandler : IRequestHandler<PingCommand>
{
    public async Task Handle(PingCommand request, CancellationToken cancellationToken)
    {
        // Simulate minimal work
        await Task.CompletedTask;
    }
}

[FoundatioIgnore]
public class MediatRGreetingHandler : IRequestHandler<GreetingQuery, string>
{
    public Task<string> Handle(GreetingQuery request, CancellationToken cancellationToken)
    {
        return Task.FromResult($"Hello, {request.Name}!");
    }
}

[FoundatioIgnore]
public class MediatRCreateOrderHandler : IRequestHandler<CreateOrderCommand, string>
{
    public async Task<string> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // Simulate order creation
        await Task.CompletedTask;
        return $"Order-{Guid.NewGuid():N}";
    }
}

[FoundatioIgnore]
public class MediatRGetOrderDetailsHandler : IRequestHandler<GetOrderDetailsQuery, OrderDetails>
{
    public async Task<OrderDetails> Handle(GetOrderDetailsQuery request, CancellationToken cancellationToken)
    {
        // Simulate database lookup
        await Task.CompletedTask;
        return new OrderDetails(
            request.OrderId,
            "Product-123",
            1,
            99.99m,
            "Customer-456",
            DateTime.UtcNow
        );
    }
}

// Multiple handlers for the same notification (publish scenario)
[FoundatioIgnore]
public class MediatRUserRegisteredEmailHandler : INotificationHandler<UserRegisteredEvent>
{
    public async Task Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Simulate sending email
        await Task.Delay(1, cancellationToken);
    }
}

[FoundatioIgnore]
public class MediatRUserRegisteredAnalyticsHandler : INotificationHandler<UserRegisteredEvent>
{
    public async Task Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Simulate analytics tracking
        await Task.Delay(1, cancellationToken);
    }
}

[FoundatioIgnore]
public class MediatRUserRegisteredWelcomeHandler : INotificationHandler<UserRegisteredEvent>
{
    public async Task Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        // Simulate welcome message
        await Task.Delay(1, cancellationToken);
    }
}
