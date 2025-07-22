using System.Reflection;

namespace Foundatio.Mediator;

/// <summary>
/// Represents the result of an operation without a return value.
/// </summary>
public class Result : IResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Result"/> class.
    /// </summary>
    protected Result()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Result"/> class with a status.
    /// </summary>
    /// <param name="status">The result status.</param>
    protected Result(ResultStatus status)
    {
        Status = status;
    }

    /// <summary>
    /// Gets the status of the result.
    /// </summary>
    public ResultStatus Status { get; protected set; } = ResultStatus.Ok;

    /// <summary>
    /// Gets a value indicating whether the result represents a successful operation.
    /// </summary>
    public bool IsSuccess => Status == ResultStatus.Ok || Status == ResultStatus.NoContent || Status == ResultStatus.Created;

    /// <summary>
    /// Gets the type of the result value (void for non-generic Result).
    /// </summary>
    public Type ValueType => typeof(void);

    /// <summary>
    /// Gets the success message, if any.
    /// </summary>
    public string SuccessMessage { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the correlation ID for tracking purposes.
    /// </summary>
    public string CorrelationId { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the location of a newly created resource (for Created status).
    /// </summary>
    public string Location { get; internal set; } = string.Empty;

    /// <summary>
    /// Gets the collection of error messages.
    /// </summary>
    public IEnumerable<string> Errors { get; internal set; } = new List<string>();

    /// <summary>
    /// Gets the collection of validation errors.
    /// </summary>
    public IEnumerable<ValidationError> ValidationErrors { get; internal set; } = new List<ValidationError>();

    /// <summary>
    /// Gets the result value as an object (null for non-generic Result).
    /// </summary>
    /// <returns>null for non-generic Result.</returns>
    public virtual object? GetValue() => null;

    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <returns>A successful result.</returns>
    public static Result Success() => new Result();

    /// <summary>
    /// Creates a successful result with a success message.
    /// </summary>
    /// <param name="successMessage">The success message.</param>
    /// <returns>A successful result with a success message.</returns>
    public static Result Success(string successMessage)
    {
        var result = new Result();
        result.SuccessMessage = successMessage;
        return result;
    }

    /// <summary>
    /// Creates a result indicating successful creation of a resource.
    /// </summary>
    /// <returns>A result with Created status.</returns>
    public static Result Created() => new Result(ResultStatus.Created);

    /// <summary>
    /// Creates a result indicating successful creation of a resource with a location.
    /// </summary>
    /// <param name="location">The location of the created resource.</param>
    /// <returns>A result with Created status and location.</returns>
    public static Result Created(string location)
    {
        var result = new Result(ResultStatus.Created);
        result.Location = location;
        return result;
    }

    /// <summary>
    /// Creates a result indicating no content.
    /// </summary>
    /// <returns>A result with NoContent status.</returns>
    public static Result NoContent() => new Result(ResultStatus.NoContent);

    /// <summary>
    /// Creates an error result with a single error message.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>An error result.</returns>
    public static Result Error(string errorMessage)
    {
        var result = new Result(ResultStatus.Error);
        result.Errors = new List<string> { errorMessage };
        return result;
    }

    /// <summary>
    /// Creates an error result with multiple error messages.
    /// </summary>
    /// <param name="errorMessages">The error messages.</param>
    /// <returns>An error result.</returns>
    public static Result Error(params string[] errorMessages)
    {
        var result = new Result(ResultStatus.Error);
        result.Errors = errorMessages;
        return result;
    }

    /// <summary>
    /// Creates an error result with error messages.
    /// </summary>
    /// <param name="errorMessages">The error messages.</param>
    /// <returns>An error result.</returns>
    public static Result Error(IEnumerable<string> errorMessages)
    {
        var result = new Result(ResultStatus.Error);
        result.Errors = errorMessages;
        return result;
    }

    /// <summary>
    /// Creates an invalid result with a single validation error.
    /// </summary>
    /// <param name="validationError">The validation error.</param>
    /// <returns>An invalid result.</returns>
    public static Result Invalid(ValidationError validationError)
    {
        var result = new Result(ResultStatus.Invalid);
        result.ValidationErrors = new List<ValidationError> { validationError };
        return result;
    }

    /// <summary>
    /// Creates an invalid result with multiple validation errors.
    /// </summary>
    /// <param name="validationErrors">The validation errors.</param>
    /// <returns>An invalid result.</returns>
    public static Result Invalid(params ValidationError[] validationErrors)
    {
        var result = new Result(ResultStatus.Invalid);
        result.ValidationErrors = validationErrors;
        return result;
    }

    /// <summary>
    /// Creates an invalid result with validation errors.
    /// </summary>
    /// <param name="validationErrors">The validation errors.</param>
    /// <returns>An invalid result.</returns>
    public static Result Invalid(IEnumerable<ValidationError> validationErrors)
    {
        var result = new Result(ResultStatus.Invalid);
        result.ValidationErrors = validationErrors;
        return result;
    }

    /// <summary>
    /// Creates a not found result.
    /// </summary>
    /// <returns>A not found result.</returns>
    public static Result NotFound() => new Result(ResultStatus.NotFound);

    /// <summary>
    /// Creates a not found result with error messages.
    /// </summary>
    /// <param name="errorMessages">The error messages.</param>
    /// <returns>A not found result.</returns>
    public static Result NotFound(params string[] errorMessages)
    {
        var result = new Result(ResultStatus.NotFound);
        result.Errors = errorMessages;
        return result;
    }

    /// <summary>
    /// Creates an unauthorized result.
    /// </summary>
    /// <returns>An unauthorized result.</returns>
    public static Result Unauthorized() => new Result(ResultStatus.Unauthorized);

    /// <summary>
    /// Creates an unauthorized result with error messages.
    /// </summary>
    /// <param name="errorMessages">The error messages.</param>
    /// <returns>An unauthorized result.</returns>
    public static Result Unauthorized(params string[] errorMessages)
    {
        var result = new Result(ResultStatus.Unauthorized);
        result.Errors = errorMessages;
        return result;
    }

    /// <summary>
    /// Creates a forbidden result.
    /// </summary>
    /// <returns>A forbidden result.</returns>
    public static Result Forbidden() => new Result(ResultStatus.Forbidden);

    /// <summary>
    /// Creates a forbidden result with error messages.
    /// </summary>
    /// <param name="errorMessages">The error messages.</param>
    /// <returns>A forbidden result.</returns>
    public static Result Forbidden(params string[] errorMessages)
    {
        var result = new Result(ResultStatus.Forbidden);
        result.Errors = errorMessages;
        return result;
    }

    /// <summary>
    /// Creates a conflict result.
    /// </summary>
    /// <returns>A conflict result.</returns>
    public static Result Conflict() => new Result(ResultStatus.Conflict);

    /// <summary>
    /// Creates a conflict result with error messages.
    /// </summary>
    /// <param name="errorMessages">The error messages.</param>
    /// <returns>A conflict result.</returns>
    public static Result Conflict(params string[] errorMessages)
    {
        var result = new Result(ResultStatus.Conflict);
        result.Errors = errorMessages;
        return result;
    }

    /// <summary>
    /// Creates a critical error result.
    /// </summary>
    /// <param name="errorMessages">The error messages.</param>
    /// <returns>A critical error result.</returns>
    public static Result CriticalError(params string[] errorMessages)
    {
        var result = new Result(ResultStatus.CriticalError);
        result.Errors = errorMessages;
        return result;
    }

    /// <summary>
    /// Creates an unavailable result.
    /// </summary>
    /// <param name="errorMessages">The error messages.</param>
    /// <returns>An unavailable result.</returns>
    public static Result Unavailable(params string[] errorMessages)
    {
        var result = new Result(ResultStatus.Unavailable);
        result.Errors = errorMessages;
        return result;
    }
}
