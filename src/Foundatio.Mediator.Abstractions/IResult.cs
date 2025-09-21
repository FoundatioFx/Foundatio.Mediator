namespace Foundatio.Mediator;

/// <summary>
/// Defines the common interface for all Result types.
/// </summary>
public interface IResult
{
    /// <summary>
    /// Gets the status of the result.
    /// </summary>
    ResultStatus Status { get; }

    /// <summary>
    /// Gets a value indicating whether the result represents a successful operation.
    /// </summary>
    bool IsSuccess { get; }

    /// <summary>
    /// Gets the result value as an object.
    /// </summary>
    /// <returns>The result value.</returns>
    object? GetValue();

    /// <summary>
    /// Gets the status of the result.
    /// </summary>
    string Message { get; }

    /// <summary>
    /// Gets the location of a newly created resource (for Created status).
    /// </summary>
    string Location { get; }

    /// <summary>
    /// Gets the validation errors associated with the result.
    /// </summary>
    IEnumerable<ValidationError> ValidationErrors { get; }
}
