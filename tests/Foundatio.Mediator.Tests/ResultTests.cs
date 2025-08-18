namespace Foundatio.Mediator.Tests;

public class ResultTests
{
    [Fact]
    public void Result_DefaultConstructor_CreatesSuccessfulResult()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(string.Empty, result.Message);
        Assert.Equal(string.Empty, result.Location);
        Assert.Empty(result.ValidationErrors);
        Assert.Equal(typeof(void), result.ValueType);
        Assert.Null(result.GetValue());
    }

    [Fact]
    public void Result_Success_WithMessage_CreatesSuccessfulResultWithMessage()
    {
        const string message = "Operation completed successfully";
        var result = Result.Success(message);

        Assert.True(result.IsSuccess);
        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(message, result.Message);
        Assert.Equal(string.Empty, result.Location);
        Assert.Empty(result.ValidationErrors);
    }

    [Theory]
    [InlineData(ResultStatus.Ok, true)]
    [InlineData(ResultStatus.Created, true)]
    [InlineData(ResultStatus.NoContent, true)]
    [InlineData(ResultStatus.Error, false)]
    [InlineData(ResultStatus.BadRequest, false)]
    [InlineData(ResultStatus.NotFound, false)]
    [InlineData(ResultStatus.Unauthorized, false)]
    [InlineData(ResultStatus.Forbidden, false)]
    [InlineData(ResultStatus.Conflict, false)]
    [InlineData(ResultStatus.CriticalError, false)]
    [InlineData(ResultStatus.Unavailable, false)]
    [InlineData(ResultStatus.Invalid, false)]
    public void Result_IsSuccess_ReturnsCorrectValue(ResultStatus status, bool expectedIsSuccess)
    {
        var result = status switch
        {
            ResultStatus.Ok => Result.Success(),
            ResultStatus.Created => Result.Created(),
            ResultStatus.NoContent => Result.NoContent(),
            ResultStatus.Error => Result.Error("Error"),
            ResultStatus.BadRequest => Result.BadRequest("Bad request"),
            ResultStatus.NotFound => Result.NotFound(),
            ResultStatus.Unauthorized => Result.Unauthorized(),
            ResultStatus.Forbidden => Result.Forbidden(),
            ResultStatus.Conflict => Result.Conflict(),
            ResultStatus.CriticalError => Result.CriticalError("Critical"),
            ResultStatus.Unavailable => Result.Unavailable("Unavailable"),
            ResultStatus.Invalid => Result.Invalid(new ValidationError("Field", "Error")),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        Assert.Equal(expectedIsSuccess, result.IsSuccess);
        Assert.Equal(status, result.Status);
    }

    [Theory]
    [InlineData(ResultStatus.Ok, true)]
    [InlineData(ResultStatus.Created, true)]
    [InlineData(ResultStatus.NoContent, true)]
    [InlineData(ResultStatus.Error, false)]
    [InlineData(ResultStatus.BadRequest, false)]
    [InlineData(ResultStatus.NotFound, false)]
    [InlineData(ResultStatus.Unauthorized, false)]
    [InlineData(ResultStatus.Forbidden, false)]
    [InlineData(ResultStatus.Conflict, false)]
    [InlineData(ResultStatus.CriticalError, false)]
    [InlineData(ResultStatus.Unavailable, false)]
    [InlineData(ResultStatus.Invalid, false)]
    public void ResultT_IsSuccess_ReturnsCorrectValue(ResultStatus status, bool expectedIsSuccess)
    {
        var result = status switch
        {
            ResultStatus.Ok => Result<string>.Success("value"),
            ResultStatus.Created => Result<string>.Created("value"),
            ResultStatus.NoContent => Result<string>.NoContent(),
            ResultStatus.Error => Result<string>.Error("Error"),
            ResultStatus.BadRequest => Result<string>.BadRequest("Bad request"),
            ResultStatus.NotFound => Result<string>.NotFound(),
            ResultStatus.Unauthorized => Result<string>.Unauthorized(),
            ResultStatus.Forbidden => Result<string>.Forbidden(),
            ResultStatus.Conflict => Result<string>.Conflict(),
            ResultStatus.CriticalError => Result<string>.CriticalError("Critical"),
            ResultStatus.Unavailable => Result<string>.Unavailable("Unavailable"),
            ResultStatus.Invalid => Result<string>.Invalid(new ValidationError("Field", "Error")),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        Assert.Equal(expectedIsSuccess, result.IsSuccess);
        Assert.Equal(status, result.Status);
    }

    [Fact]
    public void Result_Invalid_WithSingleValidationError_CreatesInvalidResult()
    {
        var validationError = new ValidationError("Name", "Name is required");
        var result = Result.Invalid(validationError);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(string.Empty, result.Message);
        Assert.Equal(string.Empty, result.Location);
        Assert.Single(result.ValidationErrors);
        Assert.Equal(validationError, result.ValidationErrors.First());
    }

    [Fact]
    public void Result_Invalid_WithMultipleValidationErrors_CreatesInvalidResult()
    {
        var validationErrors = new[]
        {
            new ValidationError("Name", "Name is required"),
            new ValidationError("Email", "Email is invalid")
        };
        var result = Result.Invalid(validationErrors);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(string.Empty, result.Message);
        Assert.Equal(string.Empty, result.Location);
        Assert.Equal(2, result.ValidationErrors.Count());
        Assert.Equal(validationErrors, result.ValidationErrors);
    }

    [Fact]
    public void Result_Invalid_WithValidationErrorCollection_CreatesInvalidResult()
    {
        var validationErrors = new List<ValidationError>
        {
            new("Name", "Name is required"),
            new("Email", "Email is invalid"),
            new("Age", "Age must be positive")
        };
        var result = Result.Invalid(validationErrors);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(string.Empty, result.Message);
        Assert.Equal(string.Empty, result.Location);
        Assert.Equal(3, result.ValidationErrors.Count());
        Assert.Equal(validationErrors, result.ValidationErrors);
    }

    [Fact]
    public void Result_Cast_CreatesTypedResultWithSameProperties()
    {
        const string message = "Test message";

        var originalResult = Result.Error(message);
        var convertedResult = originalResult.Cast<string>();

        Assert.Equal(originalResult.Status, convertedResult.Status);
        Assert.Equal(originalResult.Message, convertedResult.Message);
        Assert.Equal(originalResult.Location, convertedResult.Location);
        Assert.Equal(originalResult.ValidationErrors, convertedResult.ValidationErrors);
        Assert.Equal(default(string), convertedResult.Value);
    }

    [Fact]
    public void ResultT_Success_WithValue_CreatesSuccessfulResult()
    {
        const string value = "Test value";
        var result = Result<string>.Success(value);

        Assert.True(result.IsSuccess);
        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(value, result.Value);
        Assert.Equal(string.Empty, result.Message);
        Assert.Equal(string.Empty, result.Location);
        Assert.Empty(result.ValidationErrors);
        Assert.Equal(typeof(string), result.ValueType);
        Assert.Equal(value, result.GetValue());
    }

    [Fact]
    public void ResultT_Success_WithValueAndMessage_CreatesSuccessfulResultWithMessage()
    {
        const int value = 42;
        const string message = "Operation completed successfully";
        var result = Result<int>.Success(value, message);

        Assert.True(result.IsSuccess);
        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(value, result.Value);
        Assert.Equal(message, result.Message);
        Assert.Equal(string.Empty, result.Location);
        Assert.Empty(result.ValidationErrors);
    }

    [Fact]
    public void ResultT_ImplicitConversion_FromValue_CreatesSuccessfulResult()
    {
        const string value = "Test value";
        Result<string> result = value;

        Assert.True(result.IsSuccess);
        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(value, result.Value);
        Assert.Equal(string.Empty, result.Message);
        Assert.Equal(string.Empty, result.Location);
        Assert.Empty(result.ValidationErrors);
    }

    [Fact]
    public void ResultT_ImplicitConversion_ToValue_ReturnsValue()
    {
        const string value = "Test value";
        var result = Result<string>.Success(value);

        string extractedValue = result;

        Assert.Equal(value, extractedValue);
    }

    [Fact]
    public void ResultT_ImplicitConversion_FromResult_PreservesProperties()
    {
        const string message = "Test message";

        var originalResult = Result.BadRequest(message);
        Result<string> convertedResult = originalResult;

        Assert.Equal(originalResult.Status, convertedResult.Status);
        Assert.Equal(originalResult.Message, convertedResult.Message);
        Assert.Equal(originalResult.Location, convertedResult.Location);
        Assert.Equal(originalResult.ValidationErrors, convertedResult.ValidationErrors);
        Assert.Equal(default(string), convertedResult.Value);
        Assert.False(convertedResult.IsSuccess);
    }

    [Fact]
    public void ResultT_ImplicitConversion_FromResult_WithValidationErrors_PreservesProperties()
    {
        var validationError = new ValidationError("Field", "Error message");
        var originalResult = Result.Invalid(validationError);

        Result<string> convertedResult = originalResult;

        Assert.Equal(originalResult.Status, convertedResult.Status);
        Assert.Equal(originalResult.Message, convertedResult.Message);
        Assert.Equal(originalResult.Location, convertedResult.Location);
        Assert.Equal(originalResult.ValidationErrors, convertedResult.ValidationErrors);
        Assert.Equal(default(string), convertedResult.Value);
        Assert.False(convertedResult.IsSuccess);
    }

    [Fact]
    public void ResultT_FromResult_PreservesProperties()
    {
        const string message = "User not found";
        var originalResult = Result.NotFound(message);

        var convertedResult = Result<string>.FromResult(originalResult);

        Assert.Equal(originalResult.Status, convertedResult.Status);
        Assert.Equal(originalResult.Message, convertedResult.Message);
        Assert.Equal(originalResult.Location, convertedResult.Location);
        Assert.Equal(originalResult.ValidationErrors, convertedResult.ValidationErrors);
        Assert.Equal(default(string), convertedResult.Value);
        Assert.False(convertedResult.IsSuccess);
    }

    [Fact]
    public void ResultT_FromResult_WithSuccessResult_PreservesProperties()
    {
        const string location = "/api/created";
        var originalResult = Result.Created(location);

        var convertedResult = Result<int>.FromResult(originalResult);

        Assert.Equal(originalResult.Status, convertedResult.Status);
        Assert.Equal(originalResult.Message, convertedResult.Message);
        Assert.Equal(originalResult.Location, convertedResult.Location);
        Assert.Equal(originalResult.ValidationErrors, convertedResult.ValidationErrors);
        Assert.Equal(default(int), convertedResult.Value);
        Assert.True(convertedResult.IsSuccess);
    }

    [Fact]
    public void ResultT_Invalid_WithSingleValidationError_CreatesInvalidResult()
    {
        var validationError = new ValidationError("Name", "Name is required");
        var result = Result<string>.Invalid(validationError);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(default(string), result.Value);
        Assert.Equal(string.Empty, result.Message);
        Assert.Equal(string.Empty, result.Location);
        Assert.Single(result.ValidationErrors);
        Assert.Equal(validationError, result.ValidationErrors.First());
    }

    [Fact]
    public void ResultT_Invalid_WithMultipleValidationErrors_CreatesInvalidResult()
    {
        var validationErrors = new[]
        {
            new ValidationError("Name", "Name is required"),
            new ValidationError("Email", "Email is invalid")
        };
        var result = Result<string>.Invalid(validationErrors);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(default(string), result.Value);
        Assert.Equal(string.Empty, result.Message);
        Assert.Equal(string.Empty, result.Location);
        Assert.Equal(2, result.ValidationErrors.Count());
        Assert.Equal(validationErrors, result.ValidationErrors);
    }

    [Fact]
    public void ResultT_Invalid_WithValidationErrorCollection_CreatesInvalidResult()
    {
        var validationErrors = new List<ValidationError>
        {
            new("Name", "Name is required"),
            new("Email", "Email is invalid"),
            new("Age", "Age must be positive")
        };
        var result = Result<string>.Invalid(validationErrors);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(default(string), result.Value);
        Assert.Equal(string.Empty, result.Message);
        Assert.Equal(string.Empty, result.Location);
        Assert.Equal(3, result.ValidationErrors.Count());
        Assert.Equal(validationErrors, result.ValidationErrors);
    }

    [Fact]
    public void ResultT_ValueType_ReturnsCorrectType()
    {
        var stringResult = Result<string>.Success("test");
        var intResult = Result<int>.Success(42);
        var objectResult = Result<object>.Success(new object());

        Assert.Equal(typeof(string), stringResult.ValueType);
        Assert.Equal(typeof(int), intResult.ValueType);
        Assert.Equal(typeof(object), objectResult.ValueType);
    }

    [Fact]
    public void ResultT_GetValue_ReturnsValueAsObject()
    {
        const string value = "test value";
        var result = Result<string>.Success(value);

        var objectValue = result.GetValue();
        Assert.Equal(value, objectValue);
        Assert.IsType<string>(objectValue);
    }

    [Fact]
    public void ResultT_Constructor_WithValue_CreatesSuccessResult()
    {
        const int value = 123;
        var result = new Result<int>(value);

        Assert.True(result.IsSuccess);
        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(value, result.Value);
        Assert.Equal(string.Empty, result.Message);
        Assert.Equal(string.Empty, result.Location);
        Assert.Empty(result.ValidationErrors);
    }

    [Fact]
    public void ResultT_WithComplexObject_HandlesCorrectly()
    {
        var complexObject = new { Id = 1, Name = "Test", Items = new[] { 1, 2, 3 } };
        var result = Result<object>.Success(complexObject);

        Assert.True(result.IsSuccess);
        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(complexObject, result.Value);
        Assert.Equal(complexObject, result.GetValue());
    }

    [Fact]
    public void ResultT_WithNullValue_HandlesCorrectly()
    {
        var result = Result<string?>.Success(null);

        Assert.True(result.IsSuccess);
        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Null(result.Value);
        Assert.Null(result.GetValue());
    }

    [Fact]
    public void ValidationError_Constructor_SetsPropertiesCorrectly()
    {
        const string identifier = "Email";
        const string errorMessage = "Email is required";

        var validationError = new ValidationError(identifier, errorMessage);

        Assert.Equal(identifier, validationError.Identifier);
        Assert.Equal(errorMessage, validationError.ErrorMessage);
        Assert.Equal(string.Empty, validationError.ErrorCode);
        Assert.Equal(ValidationSeverity.Error, validationError.Severity);
    }

    [Fact]
    public void ValidationError_ConstructorWithErrorMessageOnly_SetsPropertiesCorrectly()
    {
        const string errorMessage = "An error occurred";

        var validationError = new ValidationError(errorMessage);

        Assert.Equal(string.Empty, validationError.Identifier);
        Assert.Equal(errorMessage, validationError.ErrorMessage);
        Assert.Equal(string.Empty, validationError.ErrorCode);
        Assert.Equal(ValidationSeverity.Error, validationError.Severity);
    }

    [Fact]
    public void ValidationError_ConstructorWithFullDetails_SetsPropertiesCorrectly()
    {
        const string identifier = "Email";
        const string errorMessage = "Email is required";
        const string errorCode = "REQUIRED_FIELD";
        const ValidationSeverity severity = ValidationSeverity.Warning;

        var validationError = new ValidationError(identifier, errorMessage, errorCode, severity);

        Assert.Equal(identifier, validationError.Identifier);
        Assert.Equal(errorMessage, validationError.ErrorMessage);
        Assert.Equal(errorCode, validationError.ErrorCode);
        Assert.Equal(severity, validationError.Severity);
    }

    [Fact]
    public void ValidationError_DefaultConstructor_SetsDefaultValues()
    {
        var validationError = new ValidationError();

        Assert.Equal(string.Empty, validationError.Identifier);
        Assert.Equal(string.Empty, validationError.ErrorMessage);
        Assert.Equal(string.Empty, validationError.ErrorCode);
        Assert.Equal(ValidationSeverity.Error, validationError.Severity);
    }

    [Fact]
    public void ValidationError_ToString_WithIdentifier_ReturnsFormattedString()
    {
        var validationError = new ValidationError("Name", "Name is required");
        var toString = validationError.ToString();

        Assert.Equal("Name: Name is required", toString);
    }

    [Fact]
    public void ValidationError_ToString_WithoutIdentifier_ReturnsErrorMessage()
    {
        var validationError = new ValidationError("An error occurred");
        var toString = validationError.ToString();

        Assert.Equal("An error occurred", toString);
    }

    [Fact]
    public void ValidationError_NullParameters_HandledCorrectly()
    {
        var validationError = new ValidationError(null!, null!);

        Assert.Equal(string.Empty, validationError.Identifier);
        Assert.Equal(string.Empty, validationError.ErrorMessage);
    }
}
