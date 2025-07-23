namespace Foundatio.Mediator;

/// <summary>
/// Represents a validation error with details about what went wrong.
/// </summary>
public class ValidationError
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationError"/> class.
    /// </summary>
    public ValidationError()
    {
        ErrorMessage = String.Empty;
        Identifier = String.Empty;
        ErrorCode = String.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationError"/> class with an error message.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    public ValidationError(string errorMessage)
    {
        ErrorMessage = errorMessage ?? String.Empty;
        Identifier = String.Empty;
        ErrorCode = String.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationError"/> class with an identifier and error message.
    /// </summary>
    /// <param name="identifier">The field or property identifier that caused the validation error.</param>
    /// <param name="errorMessage">The error message.</param>
    public ValidationError(string identifier, string errorMessage)
    {
        Identifier = identifier ?? String.Empty;
        ErrorMessage = errorMessage ?? String.Empty;
        ErrorCode = String.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ValidationError"/> class with full details.
    /// </summary>
    /// <param name="identifier">The field or property identifier that caused the validation error.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="errorCode">The error code for categorization.</param>
    /// <param name="severity">The severity level of the validation error.</param>
    public ValidationError(string identifier, string errorMessage, string errorCode, ValidationSeverity severity)
    {
        Identifier = identifier ?? String.Empty;
        ErrorMessage = errorMessage ?? String.Empty;
        ErrorCode = errorCode ?? String.Empty;
        Severity = severity;
    }

    /// <summary>
    /// Gets or sets the field or property identifier that caused the validation error.
    /// </summary>
    public string Identifier { get; set; } = String.Empty;

    /// <summary>
    /// Gets or sets the error message describing what went wrong.
    /// </summary>
    public string ErrorMessage { get; set; } = String.Empty;

    /// <summary>
    /// Gets or sets the error code for categorization purposes.
    /// </summary>
    public string ErrorCode { get; set; } = String.Empty;

    /// <summary>
    /// Gets or sets the severity level of the validation error.
    /// </summary>
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Error;

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
