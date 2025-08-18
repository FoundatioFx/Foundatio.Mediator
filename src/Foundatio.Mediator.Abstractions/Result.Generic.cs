namespace Foundatio.Mediator;

/// <summary>
/// Represents the result of an operation with a return value of type T.
/// </summary>
/// <typeparam name="T">The type of the result value.</typeparam>
public class Result<T> : IResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Result{T}"/> class.
    /// </summary>
    protected Result()
    {
        Value = default!;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Result{T}"/> class with a value.
    /// </summary>
    /// <param name="value">The result value.</param>
    public Result(T value)
    {
        Value = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Result{T}"/> class with a value and success message.
    /// </summary>
    /// <param name="value">The result value.</param>
    /// <param name="successMessage">The success message.</param>
    protected internal Result(T value, string successMessage) : this(value)
    {
        Message = successMessage;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Result{T}"/> class with a status.
    /// </summary>
    /// <param name="status">The result status.</param>
    internal Result(ResultStatus status)
    {
        Status = status;
        Value = default!;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Result{T}"/> class with a status and value.
    /// </summary>
    /// <param name="status">The result status.</param>
    /// <param name="value">The result value.</param>
    protected Result(ResultStatus status, T value)
    {
        Status = status;
        Value = value;
    }

    /// <summary>
    /// Implicit conversion from T to Result&lt;T&gt;.
    /// </summary>
    /// <param name="value">The value to convert.</param>
    public static implicit operator Result<T>(T value) => new(value);

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
        var convertedResult = new Result<T>(result.Status);
        convertedResult.Message = result.Message;
        convertedResult.Location = result.Location;
        convertedResult.ValidationErrors = result.ValidationErrors;
        return convertedResult;
    }

    /// <summary>
    /// Creates a Result&lt;T&gt; from a Result.
    /// </summary>
    /// <param name="result">The result to convert.</param>
    /// <returns>A Result&lt;T&gt; instance.</returns>
    public static Result<T> FromResult(IResult result)
    {
        var convertedResult = new Result<T>(result.Status);
        convertedResult.Message = result.Message;
        convertedResult.Location = result.Location;
        convertedResult.ValidationErrors = result.ValidationErrors;
        return convertedResult;
    }

    /// <summary>
    /// Gets the result value.
    /// </summary>
    public T Value { get; private set; } = default!;

    /// <summary>
    /// Gets the type of the result value.
    /// </summary>
    public Type ValueType => typeof(T);

    /// <summary>
    /// Gets the status of the result.
    /// </summary>
    public ResultStatus Status { get; protected set; } = ResultStatus.Ok;

    /// <summary>
    /// Gets a value indicating whether the result represents a successful operation.
    /// </summary>
    public bool IsSuccess => Status == ResultStatus.Ok || Status == ResultStatus.NoContent || Status == ResultStatus.Created;

    /// <summary>
    /// Gets the message associated with the result.
    /// </summary>
    public string Message { get; internal set; } = String.Empty;

    /// <summary>
    /// Gets the location of a newly created resource (for Created status).
    /// </summary>
    public string Location { get; internal set; } = String.Empty;

    /// <summary>
    /// Gets the collection of validation errors.
    /// </summary>
    public IEnumerable<ValidationError> ValidationErrors { get; internal set; } = new List<ValidationError>();

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
    public static Result<T> Success(T value) => new(value);

    /// <summary>
    /// Creates a successful result with a value and success message.
    /// </summary>
    /// <param name="value">The result value.</param>
    /// <param name="successMessage">The success message.</param>
    /// <returns>A successful result.</returns>
    public static Result<T> Success(T value, string successMessage) => new(value, successMessage);

    /// <summary>
    /// Creates a result indicating successful creation of a resource.
    /// </summary>
    /// <param name="value">The created resource.</param>
    /// <returns>A result with Created status.</returns>
    public static Result<T> Created(T value) => new(ResultStatus.Created, value);

    /// <summary>
    /// Creates a result indicating successful creation of a resource with a location.
    /// </summary>
    /// <param name="value">The created resource.</param>
    /// <param name="location">The location of the created resource. Could be a full path or just an identifier.</param>
    /// <returns>A result with Created status and location.</returns>
    public static Result<T> Created(T value, string location)
    {
        var result = new Result<T>(ResultStatus.Created, value);
        result.Location = location;
        return result;
    }

    /// <summary>
    /// Creates a result indicating no content.
    /// </summary>
    /// <returns>A result with NoContent status.</returns>
    public static Result<T> NoContent() => new(ResultStatus.NoContent);

    /// <summary>
    /// Creates an error result with a single error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>An error result.</returns>
    public static Result<T> Error(string message)
    {
        var result = new Result<T>(ResultStatus.Error);
        result.Message = message;
        return result;
    }

    /// <summary>
    /// Creates an invalid result with a single validation error.
    /// </summary>
    /// <param name="validationError">The validation error.</param>
    /// <returns>An invalid result.</returns>
    public static Result<T> Invalid(ValidationError validationError)
    {
        var result = new Result<T>(ResultStatus.Invalid);
        result.ValidationErrors = new List<ValidationError> { validationError };
        return result;
    }

    /// <summary>
    /// Creates an invalid result with multiple validation errors.
    /// </summary>
    /// <param name="validationErrors">The validation errors.</param>
    /// <returns>An invalid result.</returns>
    public static Result<T> Invalid(params ValidationError[] validationErrors)
    {
        var result = new Result<T>(ResultStatus.Invalid);
        result.ValidationErrors = validationErrors;
        return result;
    }

    /// <summary>
    /// Creates an invalid result with validation errors.
    /// </summary>
    /// <param name="validationErrors">The validation errors.</param>
    /// <returns>An invalid result.</returns>
    public static Result<T> Invalid(IEnumerable<ValidationError> validationErrors)
    {
        var result = new Result<T>(ResultStatus.Invalid);
        result.ValidationErrors = validationErrors;
        return result;
    }

    /// <summary>
    /// Creates a bad request result with message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A bad request result.</returns>
    public static Result<T> BadRequest(string message)
    {
        var result = new Result<T>(ResultStatus.BadRequest);
        result.Message = message;
        return result;
    }

    /// <summary>
    /// Creates a not found result.
    /// </summary>
    /// <returns>A not found result.</returns>
    public static Result<T> NotFound() => new(ResultStatus.NotFound);

    /// <summary>
    /// Creates a not found result with message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A not found result.</returns>
    public static Result<T> NotFound(string message)
    {
        var result = new Result<T>(ResultStatus.NotFound);
        result.Message = message;
        return result;
    }

    /// <summary>
    /// Creates an unauthorized result.
    /// </summary>
    /// <returns>An unauthorized result.</returns>
    public static Result<T> Unauthorized() => new(ResultStatus.Unauthorized);

    /// <summary>
    /// Creates an unauthorized result with a message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>An unauthorized result.</returns>
    public static Result<T> Unauthorized(string message)
    {
        var result = new Result<T>(ResultStatus.Unauthorized);
        result.Message = message;
        return result;
    }

    /// <summary>
    /// Creates a forbidden result.
    /// </summary>
    /// <returns>A forbidden result.</returns>
    public static Result<T> Forbidden() => new(ResultStatus.Forbidden);

    /// <summary>
    /// Creates a forbidden result with a message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A forbidden result.</returns>
    public static Result<T> Forbidden(string message)
    {
        var result = new Result<T>(ResultStatus.Forbidden);
        result.Message = message;
        return result;
    }

    /// <summary>
    /// Creates a conflict result.
    /// </summary>
    /// <returns>A conflict result.</returns>
    public static Result<T> Conflict() => new(ResultStatus.Conflict);

    /// <summary>
    /// Creates a conflict result with a message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A conflict result.</returns>
    public static Result<T> Conflict(string message)
    {
        var result = new Result<T>(ResultStatus.Conflict);
        result.Message = message;
        return result;
    }

    /// <summary>
    /// Creates a critical error result.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>A critical error result.</returns>
    public static Result<T> CriticalError(string message)
    {
        var result = new Result<T>(ResultStatus.CriticalError);
        result.Message = message;
        return result;
    }

    /// <summary>
    /// Creates an unavailable result.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>An unavailable result.</returns>
    public static Result<T> Unavailable(string message)
    {
        var result = new Result<T>(ResultStatus.Unavailable);
        result.Message = message;
        return result;
    }
}
