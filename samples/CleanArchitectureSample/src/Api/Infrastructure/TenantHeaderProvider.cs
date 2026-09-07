using Common.Module;
using Foundatio.Mediator;
using Foundatio.Mediator.Distributed;

namespace Api.Infrastructure;

/// <summary>
/// Carries the tenant across the queue. On enqueue the tenant comes from the request's <c>X-Tenant</c> header
/// (or from the ambient <see cref="TenantContext.Current"/> when a worker enqueues follow-on work); on the
/// worker it is restored into the handler's <see cref="CallContext"/>, where any handler can take it as a
/// <c>TenantContext</c> parameter, and made ambient for anything the handler sends on.
/// </summary>
public sealed class TenantHeaderProvider(IHttpContextAccessor httpContextAccessor) : IQueueHeaderProvider
{
    public const string TenantHttpHeader = "X-Tenant";
    private const string TenantMessageHeader = "app-tenant";
    private const string UserMessageHeader = "app-user";

    public static TenantContext Resolve(HttpContext? httpContext)
    {
        if (httpContext is null)
            return TenantContext.Current ?? TenantContext.System;

        var tenantId = httpContext.Request.Headers[TenantHttpHeader].FirstOrDefault();
        var user = httpContext.User.Identity?.Name;

        return new TenantContext(
            string.IsNullOrWhiteSpace(tenantId) ? TenantContext.DefaultTenantId : tenantId.Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(user) ? "anonymous" : user);
    }

    /// <summary>Recorded on tracked jobs at enqueue time through <c>DistributedQueueOptions.JobMetadataProvider</c>.</summary>
    public static IReadOnlyDictionary<string, string> JobMetadata(HttpContext? httpContext)
    {
        var tenant = Resolve(httpContext);
        return new Dictionary<string, string>
        {
            ["tenant"] = tenant.TenantId,
            ["user"] = tenant.User
        };
    }

    public void Enrich(object message, IDictionary<string, string> headers)
    {
        var tenant = Resolve(httpContextAccessor.HttpContext);
        headers[TenantMessageHeader] = tenant.TenantId;
        headers[UserMessageHeader] = tenant.User;
    }

    public void Restore(IReadOnlyDictionary<string, string> headers, CallContext callContext)
    {
        var tenant = new TenantContext(
            headers.GetValueOrDefault(TenantMessageHeader) is { Length: > 0 } tenantId ? tenantId : TenantContext.DefaultTenantId,
            headers.GetValueOrDefault(UserMessageHeader) is { Length: > 0 } user ? user : TenantContext.System.User);

        callContext.Set(tenant);
        TenantContext.Current = tenant;
    }
}
