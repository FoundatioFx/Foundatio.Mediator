namespace Foundatio.Mediator;

/// <summary>
/// Represents a validation error with details about what went wrong.
/// </summary>
public sealed record ValidationError
{
    /// <summary>
    /// Gets the field or property identifier that caused the validation error.
    /// </summary>
    public string Identifier { get; init; } = String.Empty;

    /// <summary>
    /// Gets the error message describing what went wrong.
    /// </summary>
    public string ErrorMessage { get; init; } = String.Empty;

    /// <summary>
    /// Gets the error code for categorization purposes.
    /// </summary>
    public string ErrorCode { get; init; } = String.Empty;

    /// <summary>
    /// Gets the severity level of the validation error.
    /// </summary>
    public ValidationSeverity Severity { get; init; } = ValidationSeverity.Error;

    /// <summary>
    /// Creates a validation error with an error message.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>A new validation error.</returns>
    public static ValidationError Create(string errorMessage)
        => new() { ErrorMessage = errorMessage ?? String.Empty };

    /// <summary>
    /// Creates a validation error with an identifier and error message.
    /// </summary>
    /// <param name="identifier">The field or property identifier that caused the validation error.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>A new validation error.</returns>
    public static ValidationError Create(string identifier, string errorMessage)
        => new() { Identifier = identifier ?? String.Empty, ErrorMessage = errorMessage ?? String.Empty };

    /// <summary>
    /// Returns a string representation of the validation error.
    /// </summary>
    /// <returns>A string representation of the validation error.</returns>
    public override string ToString()
    {
        if (!String.IsNullOrEmpty(Identifier))
            return $"{Identifier}: {ErrorMessage}";

        return ErrorMessage;
    }
}
