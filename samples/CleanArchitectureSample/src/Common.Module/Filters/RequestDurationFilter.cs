using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Common.Module.Filters;

/// <summary>
/// An endpoint filter that adds an <c>X-Request-Duration-Ms</c> response header with the
/// time spent executing the endpoint.
///
/// Endpoint filters are the right extension point for HTTP-shaped concerns — response
/// headers, per-group rate limiting, caching. To enrich the <em>message</em> itself, use
/// mediator middleware with <c>HandlerResult.ContinueWith</c> instead (see
/// <c>SetRequestedByMiddleware</c>), which also covers non-HTTP dispatch and messages
/// constructed from route/query parameters.
///
/// Wire this filter at any level of the three-tier hierarchy:
/// <list type="bullet">
///   <item><description>Global: <c>[assembly: MediatorConfiguration(EndpointFilters = [typeof(RequestDurationFilter)])]</c></description></item>
///   <item><description>Group: <c>[HandlerEndpointGroup("Orders", EndpointFilters = [typeof(RequestDurationFilter)])]</c></description></item>
///   <item><description>Endpoint: <c>[HandlerEndpoint(EndpointFilters = [typeof(RequestDurationFilter)])]</c></description></item>
/// </list>
/// </summary>
public class RequestDurationFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        long startTimestamp = Stopwatch.GetTimestamp();

        // OnStarting runs just before response headers are written, so the measurement
        // also covers result execution and works for streaming responses.
        context.HttpContext.Response.OnStarting(() =>
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            context.HttpContext.Response.Headers["X-Request-Duration-Ms"] = elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture);
            return Task.CompletedTask;
        });

        return await next(context);
    }
}
