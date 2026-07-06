using Foundatio.Mediator;
using Microsoft.AspNetCore.Http;

namespace Common.Module.Middleware;

/// <summary>
/// Enriches messages implementing <see cref="IHasRequestedBy"/> with the authenticated
/// user's identity before the handler (and validation) runs.
///
/// Because messages are immutable records, the middleware returns an enriched copy via
/// <see cref="HandlerResult.ContinueWith"/> instead of mutating the message in place.
///
/// This runs on <em>every</em> dispatch path. When the message arrives through a generated
/// endpoint, the <see cref="HttpContext"/> parameter resolves from the call context; for
/// non-HTTP dispatch (in-process <c>InvokeAsync</c>, queue consumers) it is <c>null</c> and
/// the caller is responsible for setting <c>RequestedBy</c>.
/// </summary>
[Middleware(OrderBefore = [typeof(ValidationMiddleware)])]
public static class SetRequestedByMiddleware
{
    public static HandlerResult Before(IHasRequestedBy message, HttpContext? httpContext)
    {
        if (httpContext is null)
            return HandlerResult.Continue();

        // All generated endpoints require authentication (AuthorizationRequired = true),
        // so the user is always authenticated by the time this runs.
        return HandlerResult.ContinueWith(message.WithRequestedBy(httpContext.User.Identity?.Name ?? "anonymous"));
    }
}
