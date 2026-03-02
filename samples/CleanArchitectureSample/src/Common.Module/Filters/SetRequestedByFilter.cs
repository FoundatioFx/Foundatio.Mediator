using Microsoft.AspNetCore.Http;

namespace Common.Module.Filters;

/// <summary>
/// An endpoint filter that populates <see cref="IHasRequestedBy.RequestedBy"/> on the
/// incoming message from the authenticated user's identity.
///
/// Because all generated endpoints require authentication (via
/// <c>[assembly: MediatorConfiguration(AuthorizationRequired = true)]</c>), the user is always
/// authenticated by the time this filter runs.
///
/// This demonstrates how Foundatio.Mediator's endpoint filter support lets you enrich
/// messages with HTTP-specific context <em>before</em> they reach the handler, while
/// keeping handlers completely decoupled from ASP.NET Core.
///
/// Wire this filter at any level of the three-tier hierarchy:
/// <list type="bullet">
///   <item><description>Global: <c>[assembly: MediatorConfiguration(EndpointFilters = [typeof(SetRequestedByFilter)])]</c></description></item>
///   <item><description>Category: <c>[HandlerCategory("Orders", EndpointFilters = [typeof(SetRequestedByFilter)])]</c></description></item>
///   <item><description>Endpoint: <c>[HandlerEndpoint(EndpointFilters = [typeof(SetRequestedByFilter)])]</c></description></item>
/// </list>
/// </summary>
public class SetRequestedByFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // Find the first argument that implements IHasRequestedBy
        foreach (var argument in context.Arguments)
        {
            if (argument is IHasRequestedBy message)
            {
                // Set from the authenticated user's display name
                message.RequestedBy = context.HttpContext.User.Identity?.Name ?? "anonymous";
                break;
            }
        }

        return await next(context);
    }
}
