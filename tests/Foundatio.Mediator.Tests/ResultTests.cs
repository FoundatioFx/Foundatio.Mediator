using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests;

public class ResultTests
{
    [Fact]
    public void MediatorResultMapperOptions_MapStatus_MapsConfiguredStatus()
    {
        var options = new MediatorResultMapperOptions<string>()
            .MapStatus(ResultStatus.Invalid, result => $"invalid:{result.ValidationErrors.Count()}");

        var mapped = options.TryMap(Result.Invalid(ValidationError.Create("Name", "Required")), out var mappedResult);

        Assert.True(mapped);
        Assert.Equal("invalid:1", mappedResult);
    }

    [Fact]
    public void ConfigureMediatorResultMapping_CombinesConfiguredStatuses()
    {
        var services = new ServiceCollection();

        services.ConfigureMediatorResultMapping<string>(options =>
            options.MapStatus(ResultStatus.Invalid, _ => "invalid"));
        services.ConfigureMediatorResultMapping<string>(options =>
            options.MapStatus(ResultStatus.NotFound, _ => "missing"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MediatorResultMapperOptions<string>>();

        Assert.True(options.TryMap(Result.Invalid(ValidationError.Create("Name", "Required")), out var invalidResult));
        Assert.Equal("invalid", invalidResult);
        Assert.True(options.TryMap(Result.NotFound(), out var notFoundResult));
        Assert.Equal("missing", notFoundResult);
        Assert.False(options.TryMap(Result.Success(), out _));
    }

    [Fact]
    public void ConfigureMediatorResultMapping_UsesExistingInstanceRegistration()
    {
        var services = new ServiceCollection();
        var registeredOptions = new MediatorResultMapperOptions<string>();
        services.AddSingleton(registeredOptions);

        services.ConfigureMediatorResultMapping<string>(options =>
            options.MapStatus(ResultStatus.Invalid, _ => "invalid"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MediatorResultMapperOptions<string>>();

        Assert.Same(registeredOptions, options);
        Assert.True(options.TryMap(Result.Invalid(ValidationError.Create("Name", "Required")), out var mappedResult));
        Assert.Equal("invalid", mappedResult);
    }

    [Fact]
    public void ConfigureMediatorResultMapping_WithNonInstanceRegistration_Throws()
    {
        var services = new ServiceCollection();
        services.AddSingleton<MediatorResultMapperOptions<string>>();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.ConfigureMediatorResultMapping<string>(options =>
                options.MapStatus(ResultStatus.Invalid, _ => "invalid")));

        Assert.Contains("registered as an instance", exception.Message);
    }

    [Fact]
    public void MediatorResultMapperOptions_MapValue_MapsByValueType()
    {
        var options = new MediatorResultMapperOptions<string>()
            .MapValue<Paged>(paged => $"paged:{paged.Count}");

        var mapped = options.TryMap(Result<Paged>.Success(new Paged(7)), out var mappedResult);

        Assert.True(mapped);
        Assert.Equal("paged:7", mappedResult);
    }

    [Fact]
    public void MediatorResultMapperOptions_MapValue_MatchesAssignableInterface()
    {
        var options = new MediatorResultMapperOptions<string>()
            .MapValue<IPaged>(paged => $"i:{paged.Count}");

        var mapped = options.TryMap(Result<Paged>.Success(new Paged(3)), out var mappedResult);

        Assert.True(mapped);
        Assert.Equal("i:3", mappedResult);
    }

    [Fact]
    public void MediatorResultMapperOptions_MapValue_TakesPrecedenceOverStatusMapper()
    {
        var options = new MediatorResultMapperOptions<string>()
            .MapStatus(ResultStatus.Ok, _ => "status")
            .MapValue<Paged>(paged => $"paged:{paged.Count}");

        var mapped = options.TryMap(Result<Paged>.Success(new Paged(1)), out var mappedResult);

        Assert.True(mapped);
        Assert.Equal("paged:1", mappedResult);
    }

    [Fact]
    public void MediatorResultMapperOptions_MapValue_FirstRegisteredMatchWins()
    {
        var options = new MediatorResultMapperOptions<string>()
            .MapValue<IPaged>(_ => "first")
            .MapValue<Paged>(_ => "second");

        var mapped = options.TryMap(Result<Paged>.Success(new Paged(1)), out var mappedResult);

        Assert.True(mapped);
        Assert.Equal("first", mappedResult);
    }

    [Fact]
    public void MediatorResultMapperOptions_MapValue_Conditional_OnlyMapsWhenPredicateMatches()
    {
        var options = new MediatorResultMapperOptions<int>()
            .MapValue<Paged>(when: paged => paged.Count > 0, map: paged => paged.Count);

        Assert.True(options.TryMap(Result<Paged>.Success(new Paged(5)), out var matched));
        Assert.Equal(5, matched);

        // Predicate fails and no status mapper is configured for Ok, so nothing maps.
        Assert.False(options.TryMap(Result<Paged>.Success(new Paged(0)), out _));
    }

    [Fact]
    public void MediatorResultMapperOptions_MapValue_FallsBackToStatusMapperWhenNoValueMatch()
    {
        var options = new MediatorResultMapperOptions<string>()
            .MapValue<Paged>(_ => "paged")
            .MapStatus(ResultStatus.NotFound, _ => "notfound");

        var mapped = options.TryMap(Result.NotFound(), out var mappedResult);

        Assert.True(mapped);
        Assert.Equal("notfound", mappedResult);
    }

    [Fact]
    public void MediatorResultMapperOptions_MapValue_NullArguments_Throw()
    {
        var options = new MediatorResultMapperOptions<string>();

        Assert.Throws<ArgumentNullException>(() => options.MapValue<Paged>(null!));
        Assert.Throws<ArgumentNullException>(() => options.MapValue<Paged>(when: null!, map: _ => "x"));
        Assert.Throws<ArgumentNullException>(() => options.MapValue<Paged>(when: _ => true, map: null!));
    }

    private interface IPaged
    {
        int Count { get; }
    }

    private sealed record Paged(int Count) : IPaged;

    [Fact]
    public void Result_DefaultConstructor_CreatesSuccessfulResult()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(string.Empty, result.Message);
        Assert.Equal(string.Empty, result.Location);
        Assert.Empty(result.ValidationErrors);
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
            ResultStatus.Invalid => Result.Invalid(ValidationError.Create("Field", "Error")),
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
            ResultStatus.Invalid => Result<string>.Invalid(ValidationError.Create("Field", "Error")),
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };

        Assert.Equal(expectedIsSuccess, result.IsSuccess);
        Assert.Equal(status, result.Status);
    }

    [Fact]
    public void Result_Invalid_WithSingleValidationError_CreatesInvalidResult()
    {
        var validationError = ValidationError.Create("Name", "Name is required");
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
            ValidationError.Create("Name", "Name is required"),
            ValidationError.Create("Email", "Email is invalid")
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
            ValidationError.Create("Name", "Name is required"),
            ValidationError.Create("Email", "Email is invalid"),
            ValidationError.Create("Age", "Age must be positive")
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
        Assert.Throws<InvalidOperationException>(() => _ = convertedResult.Value);
        Assert.Equal(default(string), convertedResult.ValueOrDefault);
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
    public void ResultT_ImplicitConversion_ToNullableValue_ReturnsValue()
    {
        const string value = "Test value";
        var result = Result<string>.Success(value);

        string? extractedValue = result;

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
        Assert.Throws<InvalidOperationException>(() => _ = convertedResult.Value);
        Assert.Equal(default(string), convertedResult.ValueOrDefault);
        Assert.False(convertedResult.IsSuccess);
    }

    [Fact]
    public void ResultT_ImplicitConversion_FromResult_WithValidationErrors_PreservesProperties()
    {
        var validationError = ValidationError.Create("Field", "Error message");
        var originalResult = Result.Invalid(validationError);

        Result<string> convertedResult = originalResult;

        Assert.Equal(originalResult.Status, convertedResult.Status);
        Assert.Equal(originalResult.Message, convertedResult.Message);
        Assert.Equal(originalResult.Location, convertedResult.Location);
        Assert.Equal(originalResult.ValidationErrors, convertedResult.ValidationErrors);
        Assert.Throws<InvalidOperationException>(() => _ = convertedResult.Value);
        Assert.Equal(default(string), convertedResult.ValueOrDefault);
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
        Assert.Throws<InvalidOperationException>(() => _ = convertedResult.Value);
        Assert.Equal(default(string), convertedResult.ValueOrDefault);
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
        var validationError = ValidationError.Create("Name", "Name is required");
        var result = Result<string>.Invalid(validationError);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
        Assert.Equal(default(string), result.ValueOrDefault);
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
            ValidationError.Create("Name", "Name is required"),
            ValidationError.Create("Email", "Email is invalid")
        };
        var result = Result<string>.Invalid(validationErrors);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
        Assert.Equal(default(string), result.ValueOrDefault);
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
            ValidationError.Create("Name", "Name is required"),
            ValidationError.Create("Email", "Email is invalid"),
            ValidationError.Create("Age", "Age must be positive")
        };
        var result = Result<string>.Invalid(validationErrors);

        Assert.False(result.IsSuccess);
        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
        Assert.Equal(default(string), result.ValueOrDefault);
        Assert.Equal(string.Empty, result.Message);
        Assert.Equal(string.Empty, result.Location);
        Assert.Equal(3, result.ValidationErrors.Count());
        Assert.Equal(validationErrors, result.ValidationErrors);
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
        var result = Result<int>.Success(value);

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
    public void ValidationError_Create_WithIdentifierAndMessage_SetsPropertiesCorrectly()
    {
        const string identifier = "Email";
        const string errorMessage = "Email is required";

        var validationError = ValidationError.Create(identifier, errorMessage);

        Assert.Equal(identifier, validationError.Identifier);
        Assert.Equal(errorMessage, validationError.ErrorMessage);
        Assert.Equal(string.Empty, validationError.ErrorCode);
        Assert.Equal(ValidationSeverity.Error, validationError.Severity);
    }

    [Fact]
    public void ValidationError_Create_WithErrorMessageOnly_SetsPropertiesCorrectly()
    {
        const string errorMessage = "An error occurred";

        var validationError = ValidationError.Create(errorMessage);

        Assert.Equal(string.Empty, validationError.Identifier);
        Assert.Equal(errorMessage, validationError.ErrorMessage);
        Assert.Equal(string.Empty, validationError.ErrorCode);
        Assert.Equal(ValidationSeverity.Error, validationError.Severity);
    }

    [Fact]
    public void ValidationError_Create_WithFullDetails_SetsPropertiesCorrectly()
    {
        const string identifier = "Email";
        const string errorMessage = "Email is required";
        const string errorCode = "REQUIRED_FIELD";
        const ValidationSeverity severity = ValidationSeverity.Warning;

        var validationError = ValidationError.Create(identifier, errorMessage) with { ErrorCode = errorCode, Severity = severity };

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
        var validationError = ValidationError.Create("Name", "Name is required");
        var toString = validationError.ToString();

        Assert.Equal("Name: Name is required", toString);
    }

    [Fact]
    public void ValidationError_ToString_WithoutIdentifier_ReturnsErrorMessage()
    {
        var validationError = ValidationError.Create("An error occurred");
        var toString = validationError.ToString();

        Assert.Equal("An error occurred", toString);
    }

    [Fact]
    public void ValidationError_NullParameters_HandledCorrectly()
    {
        var validationError = ValidationError.Create(null!, null!);

        Assert.Equal(string.Empty, validationError.Identifier);
        Assert.Equal(string.Empty, validationError.ErrorMessage);
    }

    [Fact]
    public void ValidationError_WithExpression_CreatesModifiedCopy()
    {
        var original = ValidationError.Create("Name", "Name is required");
        var modified = original with { Severity = ValidationSeverity.Warning };

        Assert.Equal("Name", modified.Identifier);
        Assert.Equal("Name is required", modified.ErrorMessage);
        Assert.Equal(ValidationSeverity.Warning, modified.Severity);
        Assert.Equal(ValidationSeverity.Error, original.Severity);
    }

    [Fact]
    public void ValidationError_RecordEquality_WorksCorrectly()
    {
        var error1 = ValidationError.Create("Name", "Name is required");
        var error2 = ValidationError.Create("Name", "Name is required");
        var error3 = ValidationError.Create("Email", "Email is invalid");

        Assert.Equal(error1, error2);
        Assert.NotEqual(error1, error3);
    }

    [Fact]
    public void Result_File_WithStream_CreatesSuccessfulFileResult()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var result = Result.File(stream, "text/csv", "report.csv");

        Assert.True(result.IsSuccess);
        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Same(stream, result.Value.Stream);
        Assert.Equal("text/csv", result.Value.ContentType);
        Assert.Equal("report.csv", result.Value.FileName);
    }

    [Fact]
    public void Result_File_WithStream_NoFileName_SetsFileNameToNull()
    {
        using var stream = new MemoryStream([4, 5, 6]);
        var result = Result.File(stream, "application/pdf");

        Assert.True(result.IsSuccess);
        Assert.Same(stream, result.Value.Stream);
        Assert.Equal("application/pdf", result.Value.ContentType);
        Assert.Null(result.Value.FileName);
    }

    [Fact]
    public void Result_File_WithBytes_CreatesSuccessfulFileResult()
    {
        byte[] bytes = [10, 20, 30];
        var result = Result.File(bytes, "application/octet-stream", "data.bin");

        Assert.True(result.IsSuccess);
        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal("application/octet-stream", result.Value.ContentType);
        Assert.Equal("data.bin", result.Value.FileName);

        using var reader = new MemoryStream();
        result.Value.Stream.CopyTo(reader);
        Assert.Equal(bytes, reader.ToArray());
    }

    [Fact]
    public void Result_File_WithBytes_NoFileName_SetsFileNameToNull()
    {
        byte[] bytes = [7, 8, 9];
        var result = Result.File(bytes, "image/png");

        Assert.True(result.IsSuccess);
        Assert.Equal("image/png", result.Value.ContentType);
        Assert.Null(result.Value.FileName);
    }

    [Fact]
    public void FileResult_DefaultValues_AreCorrect()
    {
        var fileResult = new FileResult();

        Assert.Same(Stream.Null, fileResult.Stream);
        Assert.Equal("application/octet-stream", fileResult.ContentType);
        Assert.Null(fileResult.FileName);
    }

    public record Widget(int Id, string Name);

    [Fact]
    public void Created_WithValue_InfersTypeAndSetsValue()
    {
        var widget = new Widget(1, "Gear");

        // No explicit Result<Widget> — T is inferred from the argument
        var result = Result.Created(widget);

        Assert.True(result.IsSuccess);
        Assert.Equal(ResultStatus.Created, result.Status);
        Assert.Same(widget, result.Value);
        Assert.Empty(result.Location);
    }

    [Fact]
    public void Created_WithValueAndLocation_SetsBoth()
    {
        var widget = new Widget(1, "Gear");

        var result = Result.Created(widget, "/widgets/1");

        Assert.Equal(ResultStatus.Created, result.Status);
        Assert.Same(widget, result.Value);
        Assert.Equal("/widgets/1", result.Location);
    }

    [Fact]
    public void Ok_WithValue_InfersTypeAndSetsValue()
    {
        var widget = new Widget(2, "Sprocket");

        var result = Result.Ok(widget);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Same(widget, result.Value);
    }

    [Fact]
    public void Ok_WithValueAndMessage_SetsBoth()
    {
        var widget = new Widget(2, "Sprocket");

        var result = Result.Ok(widget, "found it");

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Same(widget, result.Value);
        Assert.Equal("found it", result.Message);
    }

    [Fact]
    public void Success_WithValue_IsAliasForOk()
    {
        var widget = new Widget(3, "Cog");

        var result = Result.Success(widget);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Same(widget, result.Value);
    }

    [Fact]
    public void Success_WithValueAndMessage_SetsBoth()
    {
        var widget = new Widget(3, "Cog");

        var result = Result.Success(widget, "done");

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Same(widget, result.Value);
        Assert.Equal("done", result.Message);
    }

    [Fact]
    public void Created_WithStringArgument_StillBindsToLocationOverload()
    {
        // Documents overload resolution for T = string: the non-generic Created(string location)
        // is preferred over Created<T>(T value), so the argument is a location, not a value.
        // Use Result<string>.Created(value) when a string *value* is intended.
        Result result = Result.Created("/orders/1");

        Assert.Equal(ResultStatus.Created, result.Status);
        Assert.Equal("/orders/1", result.Location);
    }

    [Fact]
    public void Ok_WithStringArgument_StillBindsToMessageOverload()
    {
        // Same string caveat as Created: Ok(string message) wins over Ok<T>(T value).
        Result result = Result.Ok("all good");

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal("all good", result.Message);
    }

    [Fact]
    public void Success_WithStringArgument_StillBindsToMessageOverload()
    {
        // Same string caveat as Ok: Success(string successMessage) wins over Success<T>(T value).
        Result result = Result.Success("saved");

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal("saved", result.Message);
    }

    [Fact]
    public void Invalid_WithFieldKeyedErrors_CreatesValidationErrorPerMessage()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["Name"] = ["Name is required", "Name is too short"],
            ["Age"] = ["Age must be positive"]
        };

        var result = Result.Invalid(errors);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Collection(result.ValidationErrors.OrderBy(e => e.Identifier).ThenBy(e => e.ErrorMessage),
            e => { Assert.Equal("Age", e.Identifier); Assert.Equal("Age must be positive", e.ErrorMessage); },
            e => { Assert.Equal("Name", e.Identifier); Assert.Equal("Name is required", e.ErrorMessage); },
            e => { Assert.Equal("Name", e.Identifier); Assert.Equal("Name is too short", e.ErrorMessage); });
    }

    [Fact]
    public void GenericInvalid_WithFieldKeyedErrors_CreatesValidationErrorPerMessage()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["Email"] = ["Email is invalid"]
        };

        var result = Result<string>.Invalid(errors);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        var error = Assert.Single(result.ValidationErrors);
        Assert.Equal("Email", error.Identifier);
        Assert.Equal("Email is invalid", error.ErrorMessage);
    }

    [Fact]
    public void Invalid_WithEmptyDictionary_CreatesInvalidResultWithNoErrors()
    {
        var result = Result.Invalid(new Dictionary<string, string[]>());

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Empty(result.ValidationErrors);
    }

    [Fact]
    public void RateLimited_CreatesRateLimitedResult()
    {
        var result = Result.RateLimited();

        Assert.Equal(ResultStatus.RateLimited, result.Status);
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void RateLimited_WithMessage_SetsMessage()
    {
        var result = Result.RateLimited("Too many requests, retry later");

        Assert.Equal(ResultStatus.RateLimited, result.Status);
        Assert.Equal("Too many requests, retry later", result.Message);
    }

    [Fact]
    public void GenericRateLimited_CreatesRateLimitedResult()
    {
        var result = Result<int>.RateLimited("Slow down");

        Assert.Equal(ResultStatus.RateLimited, result.Status);
        Assert.False(result.IsSuccess);
        Assert.Equal("Slow down", result.Message);
    }
}
