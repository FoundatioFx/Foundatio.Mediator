namespace Foundatio.Mediator;

/// <summary>
/// Represents the status of a Result operation.
/// </summary>
public enum ResultStatus
{
    /// <summary>
    /// The operation completed successfully.
    /// </summary>
    Ok,

    /// <summary>
    /// The operation completed successfully and created a new resource.
    /// </summary>
    Created,

    /// <summary>
    /// The operation completed successfully with no content to return.
    /// </summary>
    NoContent,

    /// <summary>
    /// The request was invalid
    /// </summary>
    BadRequest,

    /// <summary>
    /// An error occurred during the operation.
    /// </summary>
    Error,

    /// <summary>
    /// The request is invalid due to validation errors.
    /// </summary>
    Invalid,

    /// <summary>
    /// The requested resource was not found.
    /// </summary>
    NotFound,

    /// <summary>
    /// The user is not authenticated.
    /// </summary>
    Unauthorized,

    /// <summary>
    /// The user is authenticated but does not have permission to perform the operation.
    /// </summary>
    Forbidden,

    /// <summary>
    /// The operation conflicts with the current state of the resource.
    /// </summary>
    Conflict,

    /// <summary>
    /// A critical error occurred that prevented the operation from completing.
    /// </summary>
    CriticalError,

    /// <summary>
    /// The service is temporarily unavailable.
    /// </summary>
    Unavailable
}
