using System.ComponentModel;

namespace Foundatio.Mediator;

/// <summary>
/// Represents the result of an operation without a return value.
/// </summary>
[ImmutableObject(true)]
public sealed class Result : IResult
{
    /// <summary>
    /// Gets the status of the result.
    /// </summary>
    public ResultStatus Status { get; init; } = ResultStatus.Success;

    /// <summary>
    /// Gets a value indicating whether the result represents a successful operation.
    /// </summary>
    public bool IsSuccess => Status == ResultStatus.Success || Status == ResultStatus.NoContent || Status == ResultStatus.Created;

    /// <summary>
    /// Gets the message associated with the result, which can be a success message or an error message.
    /// </summary>
    public string Message { get; init; } = String.Empty;

    /// <summary>
    /// Gets the location of a newly created resource (for Created status).
    /// </summary>
    public string Location { get; init; } = String.Empty;

    /// <summary>
    /// Gets the collection of validation errors.
    /// </summary>
    public IEnumerable<ValidationError> ValidationErrors { get; init; } = [];

    /// <summary>
    /// Gets the result value as an object (null for non-generic Result).
    /// </summary>
    /// <returns>null for non-generic Result.</returns>
    public object? GetValue() => null;

    /// <summary>
    /// Converts this Result to a Result&lt;T&gt; with the same status and properties but with default value.
    /// </summary>
    /// <typeparam name="T">The type of the result value.</typeparam>
    /// <returns>A Result&lt;T&gt; with the same status and properties but with default value.</returns>
    public Result<T> Cast<T>() => new()
    {
        Status = Status,
        Message = Message,
        Location = Location,
        ValidationErrors = ValidationErrors
    };

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <returns>A successful result.</returns>
    public static Result Success() => new()
    {
        Status = ResultStatus.Success
    };

    /// <summary>
    /// Creates a successful result with a success message.
    /// </summary>
    /// <param name="successMessage">The success message.</param>
    /// <returns>A successful result with a success message.</returns>
    public static Result Success(string successMessage) => new()
    {
        Status = ResultStatus.Success,
        Message = successMessage
    };

    /// <summary>
    /// Creates a result indicating successful creation of a resource.
    /// </summary>
    /// <returns>A result with Created status.</returns>
    public static Result Created() => new()
    {
        Status = ResultStatus.Created
    };

    /// <summary>
    /// Creates a result indicating successful creation of a resource with a location.
    /// </summary>
    /// <param name="location">The location of the created resource.</param>
    /// <returns>A result with Created status and location.</returns>
    public static Result Created(string location) => new()
    {
        Status = ResultStatus.Created,
        Location = location
    };

    /// <summary>
    /// Creates a result indicating no content.
    /// </summary>
    /// <returns>A result with NoContent status.</returns>
    public static Result NoContent() => new()
    {
        Status = ResultStatus.NoContent
    };

    /// <summary>
    /// Creates an error result with a single error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>An error result.</returns>
    public static Result Error(string message) => new()
    {
        Status = ResultStatus.Error,
        Message = message
    };

    /// <summary>
    /// Creates an error result from an exception.
    /// </summary>
    /// <param name="ex">The exception.</param>
    /// <returns>An error result.</returns>
    public static Result Error(Exception ex) => new()
    {
        Status = ResultStatus.Error,
        Message = ex.Message
    };

    /// <summary>
    /// Creates an invalid result with a single validation error.
    /// </summary>
    /// <param name="validationError">The validation error.</param>
    /// <returns>An invalid result.</returns>
    public static Result Invalid(ValidationError validationError) => new()
    {
        Status = ResultStatus.Invalid,
        ValidationErrors = [validationError]
    };

    /// <summary>
    /// Creates an invalid result with multiple validation errors.
    /// </summary>
    /// <param name="validationErrors">The validation errors.</param>
    /// <returns>An invalid result.</returns>
    public static Result Invalid(params IEnumerable<ValidationError> validationErrors) => new()
    {
        Status = ResultStatus.Invalid,
        ValidationErrors = [.. validationErrors]
    };

    /// <summary>
    /// Creates a bad request result with a message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A bad request result.</returns>
    public static Result BadRequest(string message) => new()
    {
        Status = ResultStatus.BadRequest,
        Message = message
    };

    /// <summary>
    /// Creates a not found result.
    /// </summary>
    /// <returns>A not found result.</returns>
    public static Result NotFound() => new()
    {
        Status = ResultStatus.NotFound
    };

    /// <summary>
    /// Creates a not found result with error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>A not found result.</returns>
    public static Result NotFound(string message) => new()
    {
        Status = ResultStatus.NotFound,
        Message = message
    };

    /// <summary>
    /// Creates an unauthorized result.
    /// </summary>
    /// <returns>An unauthorized result.</returns>
    public static Result Unauthorized() => new()
    {
        Status = ResultStatus.Unauthorized
    };

    /// <summary>
    /// Creates an unauthorized result with a message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>An unauthorized result.</returns>
    public static Result Unauthorized(string message) => new()
    {
        Status = ResultStatus.Unauthorized,
        Message = message
    };

    /// <summary>
    /// Creates a forbidden result.
    /// </summary>
    /// <returns>A forbidden result.</returns>
    public static Result Forbidden() => new()
    {
        Status = ResultStatus.Forbidden
    };

    /// <summary>
    /// Creates a forbidden result with a message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A forbidden result.</returns>
    public static Result Forbidden(string message) => new()
    {
        Status = ResultStatus.Forbidden,
        Message = message
    };

    /// <summary>
    /// Creates a conflict result.
    /// </summary>
    /// <returns>A conflict result.</returns>
    public static Result Conflict() => new()
    {
        Status = ResultStatus.Conflict
    };

    /// <summary>
    /// Creates a conflict result with a message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A conflict result.</returns>
    public static Result Conflict(string message) => new()
    {
        Status = ResultStatus.Conflict,
        Message = message
    };

    /// <summary>
    /// Creates a critical error result.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A critical error result.</returns>
    public static Result CriticalError(string message) => new()
    {
        Status = ResultStatus.CriticalError,
        Message = message
    };

    /// <summary>
    /// Creates an unavailable result.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>An unavailable result.</returns>
    public static Result Unavailable(string message) => new()
    {
        Status = ResultStatus.Unavailable,
        Message = message
    };
}
