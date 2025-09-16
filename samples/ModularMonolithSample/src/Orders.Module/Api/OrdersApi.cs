using Foundatio.Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Orders.Module.Extensions;
using Orders.Module.Handlers;
using Orders.Module.Messages;

namespace Orders.Module.Api;

public static class OrdersApi
{
    public static void MapOrdersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/orders")
            .WithTags("Orders");

        group.MapPost("/", async (CreateOrder command, IMediator mediator) =>
        {
            var result = await mediator.InvokeAsync<Result<Order>>(command);
            return result.ToCreatedResult($"/api/orders/{result.Value?.Id}");
        })
        .WithName("CreateOrder")
        .WithSummary("Create a new order");

        group.MapGet("/{orderId}", async (string orderId, IMediator mediator) =>
        {
            var result = await mediator.InvokeAsync<Result<Order>>(new GetOrder(orderId));
            return result.ToHttpResult();
        })
        .WithName("GetOrder")
        .WithSummary("Get an order by ID");

        group.MapGet("/", async (IMediator mediator) =>
        {
            var result = await mediator.InvokeAsync<Result<List<Order>>>(new GetOrders());
            return result.ToHttpResult();
        })
        .WithName("GetOrders")
        .WithSummary("Get all orders");

        group.MapPut("/{orderId}", async (string orderId, UpdateOrder request, IMediator mediator) =>
        {
            var command = request with { OrderId = orderId };
            var result = await mediator.InvokeAsync<Result<Order>>(command);
            return result.ToHttpResult();
        })
        .WithName("UpdateOrder")
        .WithSummary("Update an order");

        group.MapDelete("/{orderId}", async (string orderId, IMediator mediator) =>
        {
            var result = await mediator.InvokeAsync<Result>(new DeleteOrder(orderId));
            return result.ToHttpResult();
        })
        .WithName("DeleteOrder")
        .WithSummary("Delete an order");

        group.MapPost("/action", async (EntityAction<Order> command, IMediator mediator) =>
        {
            var foundatioMediator = (Mediator)mediator;
            foundatioMediator.ShowRegisteredHandlers();

            var result = await mediator.InvokeAsync<Result<Order>>(command);
            return result.ToCreatedResult($"/api/orders/{result.Value?.Id}");
        })
        .WithName("EntityAction")
        .WithSummary("Perform an action on an order");
    }
}
