namespace Foundatio.Mediator;

/// <summary>
/// Controls how the endpoint summary is generated from the message type name.
/// </summary>
public enum EndpointSummaryStyle
{
    /// <summary>
    /// Uses the exact message type name as-is (e.g., "GetProduct").
    /// </summary>
    Exact = 0,

    /// <summary>
    /// Splits the PascalCase message type name into space-separated words (e.g., "Get Product").
    /// </summary>
    Spaced = 1
}
