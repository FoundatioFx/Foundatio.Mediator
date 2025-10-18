using System.ComponentModel;

namespace Foundatio.Mediator;

/// <summary>
/// Represents the result of an operation with a return value of type T.
/// </summary>
/// <typeparam name="T">The type of the result value.</typeparam>
[ImmutableObject(true)]
public sealed class Result<T> : IResult
{
    /// <summary>
    /// Implicit conversion from T to Result&lt;T&gt;.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    public static implicit operator Result<T>(T value) => new()
    {
        Status = ResultStatus.Success,
        Value = value
    };

    /// <summary>
    /// Implicit conversion from Result&lt;T&gt; to T.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    public static implicit operator T(Result<T> result) => result.Value;

    /// <summary>
    /// Implicit conversion from Result to Result&lt;T&gt;.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    public static implicit operator Result<T>(Result result)
    {
        return new Result<T>
        {
            Status = result.Status,
            Message = result.Message,
            Location = result.Location,
            ValidationErrors = result.ValidationErrors
        };
    }

    /// <summary>
    /// Implicit conversion from Result&lt;T&gt; to Result.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    public static implicit operator Result(Result<T> result)
    {
        if (result == null)
            return null!;

        return new Result
        {
            Status = result.Status,
            Message = result.Message,
            Location = result.Location,
            ValidationErrors = result.ValidationErrors
        };
    }

    /// <summary>
    /// Creates a Result&lt;T&gt; from a Result.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <returns>A Result&lt;T&gt; instance.</returns>
    public static Result<T> FromResult(IResult result) => new()
    {
        Status = result.Status,
        Message = result.Message,
        Location = result.Location,
        ValidationErrors = result.ValidationErrors
    };

    /// <summary>
    /// Gets the result value.
    /// </summary>
    public T Value { get; init; } = default!;

    /// <summary>
    /// Gets the status of the result.
    /// </summary>
    public ResultStatus Status { get; init; } = ResultStatus.Success;

    /// <summary>
    /// Gets a value indicating whether the result represents a successful operation.
    /// </summary>
    public bool IsSuccess => Status == ResultStatus.Success || Status == ResultStatus.NoContent || Status == ResultStatus.Created;

    /// <summary>
    /// Gets the message associated with the result.
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
    /// Gets the result value as an object.
    /// </summary>
    /// <returns>The result value as an object.</returns>
    public object? GetValue() => Value;

    /// <summary>
    /// Creates a successful result with a value.
    /// </summary>
    /// <param name="value">The result value.</param>
    /// <returns>A successful result.</returns>
    public static Result<T> Success(T value) => new()
    {
        Status = ResultStatus.Success,
        Value = value
    };

    /// <summary>
    /// Creates a successful result with a value and success message.
    /// </summary>
    /// <param name="value">The result value.</param>
    /// <param name="successMessage">The success message.</param>
    /// <returns>A successful result.</returns>
    public static Result<T> Success(T value, string successMessage) => new()
    {
        Status = ResultStatus.Success,
        Value = value,
        Message = successMessage
    };

    /// <summary>
    /// Creates a result indicating successful creation of a resource.
    /// </summary>
    /// <param name="value">The created resource.</param>
    /// <returns>A result with Created status.</returns>
    public static Result<T> Created(T value) => new()
    {
        Status = ResultStatus.Created,
        Value = value
    };

    /// <summary>
    /// Creates a result indicating successful creation of a resource with a location.
    /// </summary>
    /// <param name="value">The created resource.</param>
    /// <param name="location">The location of the created resource. Could be a full path or just an identifier.</param>
    /// <returns>A result with Created status and location.</returns>
    public static Result<T> Created(T value, string location) => new()
    {
        Status = ResultStatus.Created,
        Value = value,
        Location = location
    };

    /// <summary>
    /// Creates a result indicating no content.
    /// </summary>
    /// <returns>A result with NoContent status.</returns>
    public static Result<T> NoContent() => new()
    {
        Status = ResultStatus.NoContent
    };

    /// <summary>
    /// Creates an error result with a single error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>An error result.</returns>
    public static Result<T> Error(string message) => new()
    {
        Status = ResultStatus.Error,
        Message = message
    };

    /// <summary>
    /// Creates an error result from an exception.
    /// </summary>
    /// <param name="ex">The exception.</param>
    /// <returns>An error result.</returns>
    public static Result<T> Error(Exception ex) => new()
    {
        Status = ResultStatus.Error,
        Message = ex.Message
    };

    /// <summary>
    /// Creates an invalid result with a single validation error.
    /// </summary>
    /// <param name="validationError">The validation error.</param>
    /// <returns>An invalid result.</returns>
    public static Result<T> Invalid(ValidationError validationError) => new()
    {
        Status = ResultStatus.Invalid,
        ValidationErrors = [validationError]
    };

    /// <summary>
    /// Creates an invalid result with multiple validation errors.
    /// </summary>
    /// <param name="validationErrors">The validation errors.</param>
    /// <returns>An invalid result.</returns>
    public static Result<T> Invalid(params IEnumerable<ValidationError> validationErrors) => new()
    {
        Status = ResultStatus.Invalid,
        ValidationErrors = [.. validationErrors]
    };

    /// <summary>
    /// Creates a bad request result with message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A bad request result.</returns>
    public static Result<T> BadRequest(string message) => new()
    {
        Status = ResultStatus.BadRequest,
        Message = message
    };

    /// <summary>
    /// Creates a not found result.
    /// </summary>
    /// <returns>A not found result.</returns>
    public static Result<T> NotFound() => new()
    {
        Status = ResultStatus.NotFound
    };

    /// <summary>
    /// Creates a not found result with message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A not found result.</returns>
    public static Result<T> NotFound(string message) => new()
    {
        Status = ResultStatus.NotFound,
        Message = message
    };

    /// <summary>
    /// Creates an unauthorized result.
    /// </summary>
    /// <returns>An unauthorized result.</returns>
    public static Result<T> Unauthorized() => new()
    {
        Status = ResultStatus.Unauthorized
    };

    /// <summary>
    /// Creates an unauthorized result with a message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>An unauthorized result.</returns>
    public static Result<T> Unauthorized(string message) => new()
    {
        Status = ResultStatus.Unauthorized,
        Message = message
    };

    /// <summary>
    /// Creates a forbidden result.
    /// </summary>
    /// <returns>A forbidden result.</returns>
    public static Result<T> Forbidden() => new()
    {
        Status = ResultStatus.Forbidden
    };

    /// <summary>
    /// Creates a forbidden result with a message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A forbidden result.</returns>
    public static Result<T> Forbidden(string message) => new()
    {
        Status = ResultStatus.Forbidden,
        Message = message
    };

    /// <summary>
    /// Creates a conflict result.
    /// </summary>
    /// <returns>A conflict result.</returns>
    public static Result<T> Conflict() => new()
    {
        Status = ResultStatus.Conflict
    };

    /// <summary>
    /// Creates a conflict result with a message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A conflict result.</returns>
    public static Result<T> Conflict(string message) => new()
    {
        Status = ResultStatus.Conflict,
        Message = message
    };

    /// <summary>
    /// Creates a critical error result.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A critical error result.</returns>
    public static Result<T> CriticalError(string message) => new()
    {
        Status = ResultStatus.CriticalError,
        Message = message
    };

    /// <summary>
    /// Creates an unavailable result.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>An unavailable result.</returns>
    public static Result<T> Unavailable(string message) => new()
    {
        Status = ResultStatus.Unavailable,
        Message = message
    };
}
