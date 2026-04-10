namespace Common.Module.Messages;

/// <summary>
/// Returns basic health and version information about the application.
/// This endpoint is public (no authentication required) — the handler
/// is marked with <c>[AllowAnonymous]</c> to opt out of the global
/// <c>RequireAuth = true</c> setting.
/// </summary>
public record GetHealthStatus;

public record HealthStatusResponse(string Status, string Version, DateTime Timestamp);
