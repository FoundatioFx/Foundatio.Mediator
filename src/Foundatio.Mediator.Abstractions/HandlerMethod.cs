namespace Foundatio.Mediator;

/// <summary>
/// Specifies the HTTP method for a generated handler endpoint.
/// </summary>
public enum HandlerMethod
{
    /// <summary>
    /// The HTTP method is inferred from the message type name prefix
    /// (e.g., Get* → GET, Create* → POST, Update* → PUT, Delete* → DELETE, Patch* → PATCH).
    /// </summary>
    Default = 0,

    /// <summary>
    /// HTTP GET — used for queries and read operations.
    /// </summary>
    Get = 1,

    /// <summary>
    /// HTTP POST — used for commands that create resources or trigger actions.
    /// </summary>
    Post = 2,

    /// <summary>
    /// HTTP PUT — used for commands that replace or fully update a resource.
    /// </summary>
    Put = 3,

    /// <summary>
    /// HTTP DELETE — used for commands that remove a resource.
    /// </summary>
    Delete = 4,

    /// <summary>
    /// HTTP PATCH — used for commands that partially update a resource.
    /// </summary>
    Patch = 5
}
