namespace Foundatio.Mediator;

/// <summary>
/// Represents the status of a Result operation.
/// </summary>
public enum ResultStatus
{
    /// <summary>
    /// The operation completed successfully.
    /// </summary>
    Ok = 0,

    /// <summary>
    /// Alias for <see cref="Ok"/>. The operation completed successfully.
    /// </summary>
    Success = Ok,

    /// <summary>
    /// The operation completed successfully and created a new resource.
    /// </summary>
    Created = 1,

    /// <summary>
    /// The request has been accepted for processing, but processing is deferred (HTTP 202).
    /// </summary>
    Accepted = 2,

    /// <summary>
    /// The operation completed successfully with no content to return.
    /// </summary>
    NoContent = 3,

    /// <summary>
    /// The request was invalid
    /// </summary>
    BadRequest = 4,

    /// <summary>
    /// An error occurred during the operation.
    /// </summary>
    Error = 5,

    /// <summary>
    /// The request is invalid due to validation errors.
    /// </summary>
    Invalid = 6,

    /// <summary>
    /// The requested resource was not found.
    /// </summary>
    NotFound = 7,

    /// <summary>
    /// The user is not authenticated.
    /// </summary>
    Unauthorized = 8,

    /// <summary>
    /// The user is authenticated but does not have permission to perform the operation.
    /// </summary>
    Forbidden = 9,

    /// <summary>
    /// The operation conflicts with the current state of the resource.
    /// </summary>
    Conflict = 10,

    /// <summary>
    /// A critical error occurred that prevented the operation from completing.
    /// </summary>
    CriticalError = 11,

    /// <summary>
    /// The service is temporarily unavailable.
    /// </summary>
    Unavailable = 12
}
