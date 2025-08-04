using Xunit;

namespace Foundatio.Mediator.Tests;

public class ResultTests
{
    public class User
    {
        public string Name { get; set; } = String.Empty;
        public int Id { get; set; }
    }

    [Fact]
    public void ExplicitCast_FromResultToGenericResult_ShouldWork()
    {
        // Arrange
        var originalResult = Result.Conflict("Resource is locked");

        // Act - This is the syntax we want to support: (Result<T>)Result.Conflict()
        var castedResult = (Result<User>)originalResult;

        // Assert
        Assert.NotNull(castedResult);
        Assert.Equal(ResultStatus.Conflict, castedResult.Status);
        Assert.False(castedResult.IsSuccess);
        Assert.Null(castedResult.Value); // Should be default(User) which is null
        Assert.Equal(typeof(User), castedResult.ValueType);

        // Verify all properties are copied
        Assert.Equal(originalResult.Message, castedResult.Message);
        Assert.Equal(originalResult.Location, castedResult.Location);
        Assert.Equal(originalResult.ValidationErrors, castedResult.ValidationErrors);
    }

    [Fact]
    public void ExplicitCast_FromResultAsObject_ShouldWork()
    {
        // Arrange
        object originalResult = Result.NotFound("Database connection failed");

        // Act - Using general-purpose TypeConverter
        if (originalResult is not Result result)
            throw new InvalidCastException($"Cannot cast {originalResult.GetType().Name} to Result<User>.");

        var castedResult = (Result<User>)result;

        // Assert
        Assert.NotNull(castedResult);
        Assert.Equal(ResultStatus.NotFound, castedResult.Status);
        Assert.False(castedResult.IsSuccess);
        Assert.Null(castedResult.Value); // Should be default(User) which is null
        Assert.Equal("Database connection failed", castedResult.Message);
    }

    [Fact]
    public void ExplicitCast_FromSuccessResult_ShouldPreserveSuccessStatus()
    {
        // Arrange
        var originalResult = Result.Success("Operation completed successfully");

        // Act
        var castedResult = (Result<User>)originalResult;

        // Assert
        Assert.NotNull(castedResult);
        Assert.Equal(ResultStatus.Ok, castedResult.Status);
        Assert.True(castedResult.IsSuccess);
        Assert.Null(castedResult.Value); // Should be default(User) which is null
        Assert.Equal("Operation completed successfully", castedResult.Message);
    }

    [Fact]
    public void ExplicitCast_FromCreatedResult_ShouldPreserveLocation()
    {
        // Arrange
        var originalResult = Result.Created("/api/users/123");

        // Act
        var castedResult = (Result<User>)originalResult;

        // Assert
        Assert.NotNull(castedResult);
        Assert.Equal(ResultStatus.Created, castedResult.Status);
        Assert.True(castedResult.IsSuccess);
        Assert.Null(castedResult.Value); // Should be default(User) which is null
        Assert.Equal("/api/users/123", castedResult.Location);
    }

    [Fact]
    public void ExplicitCast_FromErrorResult_ShouldPreserveErrors()
    {
        // Arrange
        object originalResult = Result.Error("Database connection failed");

        // Act
        if (originalResult is not Result result)
            throw new InvalidCastException($"Cannot cast {originalResult.GetType().Name} to Result<User>.");

        var castedResult = (Result<User>)result;

        // Assert
        Assert.NotNull(castedResult);
        Assert.Equal(ResultStatus.Error, castedResult.Status);
        Assert.False(castedResult.IsSuccess);
        Assert.Null(castedResult.Value); // Should be default(User) which is null
        Assert.Equal("Database connection failed", castedResult.Message);
    }

    [Fact]
    public void ExplicitCast_FromInvalidResult_ShouldPreserveValidationErrors()
    {
        // Arrange
        var validationError = new ValidationError
        {
            Identifier = "Name",
            ErrorMessage = "Name is required",
            Severity = ValidationSeverity.Error
        };
        var originalResult = Result.Invalid(validationError);

        // Act
        var castedResult = (Result<User>)originalResult;

        // Assert
        Assert.NotNull(castedResult);
        Assert.Equal(ResultStatus.Invalid, castedResult.Status);
        Assert.False(castedResult.IsSuccess);
        Assert.Null(castedResult.Value); // Should be default(User) which is null
        Assert.Single(castedResult.ValidationErrors);
        Assert.Contains(validationError, castedResult.ValidationErrors);
    }

    [Fact]
    public void ExplicitCast_FromNotFoundResult_ShouldWork()
    {
        // Arrange
        var originalResult = Result.NotFound("User not found");

        // Act
        var castedResult = (Result<User>)originalResult;

        // Assert
        Assert.NotNull(castedResult);
        Assert.Equal(ResultStatus.NotFound, castedResult.Status);
        Assert.False(castedResult.IsSuccess);
        Assert.Null(castedResult.Value); // Should be default(User) which is null
        Assert.Contains("User not found", castedResult.Message);
    }

    [Fact]
    public void ExplicitCast_ToValueType_ShouldWork()
    {
        // Arrange
        var originalResult = Result.Conflict("Value is locked");

        // Act - Cast to a value type
        var castedResult = (Result<int>)originalResult;

        // Assert
        Assert.NotNull(castedResult);
        Assert.Equal(ResultStatus.Conflict, castedResult.Status);
        Assert.False(castedResult.IsSuccess);
        Assert.Equal(0, castedResult.Value); // Should be default(int) which is 0
        Assert.Equal(typeof(int), castedResult.ValueType);
        Assert.Equal("Value is locked", castedResult.Message);
    }

    [Fact]
    public void ImplicitConversion_ShouldAlsoWork()
    {
        // Arrange
        var originalResult = Result.Success("Test successful");

        // Act - Implicit conversion should also work
        Result<User> castedResult = originalResult;

        // Assert
        Assert.NotNull(castedResult);
        Assert.Equal(ResultStatus.Ok, castedResult.Status);
        Assert.True(castedResult.IsSuccess);
        Assert.Null(castedResult.Value); // Should be default(User) which is null
        Assert.Equal("Test successful", castedResult.Message);
    }

    [Fact]
    public void ImplicitConversion_ToUser_ShouldWork()
    {
        // Arrange
        var result = Result<User>.Success(new User { Name = "John Doe", Id = 1 });

        // Act - Implicit conversion should work
        User user = result;

        // Assert
        Assert.NotNull(user);
        Assert.Equal("John Doe", user.Name);
        Assert.Equal(1, user.Id);
    }
}
