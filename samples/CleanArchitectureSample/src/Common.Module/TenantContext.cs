namespace Common.Module;

/// <summary>
/// The tenant and user a piece of work runs on behalf of. Workers restore it from message headers, so a
/// handler sees the same tenant whether it runs inline on the API node or from a queue on another host.
/// </summary>
public sealed record TenantContext(string TenantId, string User)
{
    public const string DefaultTenantId = "acme";

    public static readonly TenantContext System = new(DefaultTenantId, "system");

    private static readonly AsyncLocal<TenantContext?> s_current = new();

    /// <summary>
    /// The ambient tenant for the current async flow. Set by the worker before a queued handler runs so
    /// messages the handler enqueues or publishes inherit the tenant.
    /// </summary>
    public static TenantContext? Current
    {
        get => s_current.Value;
        set => s_current.Value = value;
    }

    public override string ToString() => $"{TenantId}/{User}";
}
