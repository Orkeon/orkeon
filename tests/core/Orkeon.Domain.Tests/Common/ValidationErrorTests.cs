using Orkeon.Domain.SharedKernel;

namespace Orkeon.Domain.Tests.Common;

/// <summary>
/// Tests for ValidationError following Clean Architecture principles.
/// Tests the business rules and validation logic of the ValidationError class.
/// </summary>
public class ValidationErrorTests
{
    [Fact]
    public void ShouldCreateValidationError_WhenConstructingWithValidParameters()
    {
        // Arrange
        var propertyName = "Email";
        var errorMessage = "Email address is required";
        var errorCode = "REQUIRED_FIELD";

        // Act
        var validationError = new ValidationError(propertyName, errorMessage, errorCode);

        // Assert
        Assert.Equal(propertyName, validationError.PropertyName);
        Assert.Equal(errorMessage, validationError.ErrorMessage);
        Assert.Equal(errorCode, validationError.ErrorCode);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullPropertyName()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ValidationError(null!, "Error message"));

        Assert.Equal("propertyName", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullErrorMessage()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ValidationError("PropertyName", null!));

        Assert.Equal("errorMessage", exception.ParamName);
    }

    [Fact]
    public void ShouldAllowNull_WhenConstructingWithNullErrorCode()
    {
        // Act
        var validationError = new ValidationError("PropertyName", "Error message", null);

        // Assert
        Assert.Null(validationError.ErrorCode);
    }

    [Fact]
    public void ShouldSetErrorCodeToNull_WhenConstructingWithoutErrorCode()
    {
        // Act
        var validationError = new ValidationError("PropertyName", "Error message");

        // Assert
        Assert.Null(validationError.ErrorCode);
    }

    [Fact]
    public void ShouldIncludeErrorCode_WhenCallingToStringWithErrorCode()
    {
        // Arrange
        var validationError = new ValidationError(
            "UserName",
            "Username must be at least 3 characters",
            "MIN_LENGTH");

        // Act
        var result = validationError.ToString();

        // Assert
        Assert.Equal("UserName: Username must be at least 3 characters (Code: MIN_LENGTH)", result);
    }

    [Fact]
    public void ShouldNotIncludeErrorCode_WhenCallingToStringWithoutErrorCode()
    {
        // Arrange
        var validationError = new ValidationError(
            "Password",
            "Password is required");

        // Act
        var result = validationError.ToString();

        // Assert
        Assert.Equal("Password: Password is required", result);
    }

    [Fact]
    public void ShouldNotIncludeErrorCode_WhenCallingToStringWithEmptyErrorCode()
    {
        // Arrange
        var validationError = new ValidationError(
            "Age",
            "Age must be positive",
            string.Empty);

        // Act
        var result = validationError.ToString();

        // Assert
        Assert.Equal("Age: Age must be positive", result);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var error1 = new ValidationError("Name", "Name is required", "REQUIRED");
        var error2 = new ValidationError("Name", "Name is required", "REQUIRED");

        // Act & Assert
        Assert.True(error1.Equals(error2));
        Assert.True(error2.Equals(error1));
        Assert.Equal(error1, error2); // Use Assert.Equal which calls Equals method
        Assert.Equal(error1.GetHashCode(), error2.GetHashCode());
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentPropertyName()
    {
        // Arrange
        var error1 = new ValidationError("Name", "Field is required", "REQUIRED");
        var error2 = new ValidationError("Email", "Field is required", "REQUIRED");

        // Act & Assert
        Assert.False(error1.Equals(error2));
        Assert.False(error2.Equals(error1));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentErrorMessage()
    {
        // Arrange
        var error1 = new ValidationError("Name", "Field is required", "REQUIRED");
        var error2 = new ValidationError("Name", "Field cannot be empty", "REQUIRED");

        // Act & Assert
        Assert.False(error1.Equals(error2));
        Assert.False(error2.Equals(error1));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentErrorCode()
    {
        // Arrange
        var error1 = new ValidationError("Name", "Field is required", "REQUIRED");
        var error2 = new ValidationError("Name", "Field is required", "MANDATORY");

        // Act & Assert
        Assert.False(error1.Equals(error2));
        Assert.False(error2.Equals(error1));
    }

    [Fact]
    public void ShouldCompareCorrectly_WhenComparingEqualityWithNullErrorCode()
    {
        // Arrange
        var error1 = new ValidationError("Name", "Field is required", null);
        var error2 = new ValidationError("Name", "Field is required", null);
        var error3 = new ValidationError("Name", "Field is required", "CODE");

        // Act & Assert
        Assert.True(error1.Equals(error2));
        Assert.False(error1.Equals(error3));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithNull()
    {
        // Arrange
        var validationError = new ValidationError("Name", "Error message");

        // Act & Assert
        Assert.False(validationError.Equals(null));
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentType()
    {
        // Arrange
        var validationError = new ValidationError("Name", "Error message");
        var otherObject = "Not a ValidationError";

        // Act & Assert
        Assert.False(validationError.Equals(otherObject));
    }

    [Fact]
    public void ShouldReturnSameHashCode_WhenCallingGetHashCodeWithSameValues()
    {
        // Arrange
        var error1 = new ValidationError("Name", "Name is required", "REQUIRED");
        var error2 = new ValidationError("Name", "Name is required", "REQUIRED");

        // Act
        var hash1 = error1.GetHashCode();
        var hash2 = error2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnDifferentHashCodes_WhenCallingGetHashCodeWithDifferentValues()
    {
        // Arrange
        var error1 = new ValidationError("Name", "Name is required", "REQUIRED");
        var error2 = new ValidationError("Email", "Email is required", "REQUIRED");

        // Act
        var hash1 = error1.GetHashCode();
        var hash2 = error2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Theory]
    [InlineData("Email", "Invalid email format", "INVALID_FORMAT")]
    [InlineData("Password", "Password too short", "MIN_LENGTH")]
    [InlineData("Age", "Age must be positive", "POSITIVE_VALUE")]
    [InlineData("UserName", "Username already exists", "DUPLICATE")]
    public void ShouldStoreCorrectly_WhenConstructingWithVariousInputs(
        string propertyName,
        string errorMessage,
        string errorCode)
    {
        // Act
        var validationError = new ValidationError(propertyName, errorMessage, errorCode);

        // Assert
        Assert.Equal(propertyName, validationError.PropertyName);
        Assert.Equal(errorMessage, validationError.ErrorMessage);
        Assert.Equal(errorCode, validationError.ErrorCode);
    }

    [Fact]
    public void ShouldWorkCorrectly_WhenUsingValidationErrorInCollection()
    {
        // Arrange
        var errors = new List<ValidationError>
        {
            new("Name", "Name is required"),
            new("Email", "Invalid email format", "INVALID_FORMAT"),
            new("Age", "Age must be positive", "POSITIVE_VALUE")
        };

        // Act
        var nameError = errors.FirstOrDefault(e => e.PropertyName == "Name");
        var emailError = errors.FirstOrDefault(e => e.ErrorCode == "INVALID_FORMAT");

        // Assert
        Assert.NotNull(nameError);
        Assert.Equal("Name is required", nameError!.ErrorMessage);
        Assert.NotNull(emailError);
        Assert.Equal("Email", emailError!.PropertyName);
    }

    [Fact]
    public void ShouldPreventDuplicates_WhenUsingValidationErrorInHashSet()
    {
        // Arrange
        var errors = new HashSet<ValidationError>();
        var error1 = new ValidationError("Name", "Name is required");
        var error2 = new ValidationError("Name", "Name is required"); // Duplicate
        var error3 = new ValidationError("Email", "Email is required");

        // Act
        errors.Add(error1);
        errors.Add(error2); // Should not be added due to equality
        errors.Add(error3);

        // Assert
        Assert.Equal(2, errors.Count);
        Assert.Contains(error1, errors);
        Assert.Contains(error3, errors);
    }

    [Fact]
    public void ShouldHandleCorrectly_WhenCallingToStringWithSpecialCharacters()
    {
        // Arrange
        var validationError = new ValidationError(
            "Field[0].Value",
            "Value must contain 'quotes' and \"double quotes\"",
            "SPECIAL_CHARS");

        // Act
        var result = validationError.ToString();

        // Assert
        Assert.Contains("Field[0].Value", result);
        Assert.Contains("'quotes'", result);
        Assert.Contains("\"double quotes\"", result);
        Assert.Contains("(Code: SPECIAL_CHARS)", result);
    }
}
