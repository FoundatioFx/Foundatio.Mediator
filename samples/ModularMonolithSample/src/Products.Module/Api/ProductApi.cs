using Common.Module;
using Foundatio.Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Common.Module.Extensions;
using Products.Module.Messages;

namespace Products.Module.Api;

public static class ProductsApi
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/products")
            .WithTags("Products");

        group.MapPost("/", async (CreateProduct command, IMediator mediator) =>
        {
            var result = await mediator.InvokeAsync<Result<Product>>(command);
            return result.ToCreatedResult($"/api/products/{result.Value?.Id}");
        })
        .WithName("CreateProduct")
        .WithSummary("Create a new product");

        group.MapGet("/{productId}", async (string productId, IMediator mediator) =>
        {
            var result = await mediator.InvokeAsync<Result<Product>>(new GetProduct(productId));
            return result.ToHttpResult();
        })
        .WithName("GetProduct")
        .WithSummary("Get a product by ID");

        group.MapGet("/", async (IMediator mediator) =>
        {
            var result = await mediator.InvokeAsync<Result<List<Product>>>(new GetProducts());
            return result.ToHttpResult();
        })
        .WithName("GetProducts")
        .WithSummary("Get all products");

        group.MapPut("/{productId}", async (string productId, UpdateProduct request, IMediator mediator) =>
        {
            var command = request with { ProductId = productId };
            var result = await mediator.InvokeAsync<Result<Product>>(command);
            return result.ToHttpResult();
        })
        .WithName("UpdateProduct")
        .WithSummary("Update a product");

        group.MapDelete("/{productId}", async (string productId, IMediator mediator) =>
        {
            var result = await mediator.InvokeAsync<Result>(new DeleteProduct(productId));
            return result.ToHttpResult();
        })
        .WithName("DeleteProduct")
        .WithSummary("Delete a product");

        group.MapPost("/action", async (EntityAction<Product> command, IMediator mediator) =>
        {
            var foundatioMediator = (Mediator)mediator;
            foundatioMediator.ShowRegisteredHandlers();

            var result = await mediator.InvokeAsync<Result<Product>>(command);
            return result.ToCreatedResult($"/api/products/{result.Value?.Id}");
        })
        .WithName("ProductEntityAction")
        .WithSummary("Perform an action on a product");
    }
}
