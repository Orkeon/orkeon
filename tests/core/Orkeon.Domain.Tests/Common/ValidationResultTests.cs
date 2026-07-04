using Orkeon.Domain.SharedKernel;

namespace Orkeon.Domain.Tests.Common;

/// <summary>
/// Tests for ValidationResult following Clean Architecture principles.
/// Tests the business rules and validation logic of the ValidationResult class.
/// </summary>
public class ValidationResultTests
{
    [Fact]
    public void ShouldCreateValidResult_WhenConstructingWithNoErrors()
    {
        // Act
        var validationResult = new ValidationResult();

        // Assert
        Assert.True(validationResult.IsValid);
        Assert.Empty(validationResult.Errors);
    }

    [Fact]
    public void ShouldCreateResultWithErrors_WhenConstructingWithParamsErrors()
    {
        // Arrange
        var error1 = new ValidationError("Name", "Name is required");
        var error2 = new ValidationError("Email", "Invalid email format");

        // Act
        var validationResult = new ValidationResult(error1, error2);

        // Assert
        Assert.False(validationResult.IsValid);
        Assert.Equal(2, validationResult.Errors.Count);
        Assert.Contains(error1, validationResult.Errors);
        Assert.Contains(error2, validationResult.Errors);
    }

    [Fact]
    public void ShouldCreateResultWithErrors_WhenConstructingWithEnumerableErrors()
    {
        // Arrange
        var errors = new List<ValidationError>
        {
            new("Property1", "Error 1"),
            new("Property2", "Error 2"),
            new("Property3", "Error 3")
        };

        // Act
        var validationResult = new ValidationResult(errors);

        // Assert
        Assert.False(validationResult.IsValid);
        Assert.Equal(3, validationResult.Errors.Count);
        Assert.Equal(errors, validationResult.Errors);
    }

    [Fact]
    public void ShouldCreateValidResult_WhenConstructingWithEmptyErrorsArray()
    {
        // Act
        var validationResult = new ValidationResult([]);

        // Assert
        Assert.True(validationResult.IsValid);
        Assert.Empty(validationResult.Errors);
    }

    [Fact]
    public void ShouldCreateValidResult_WhenConstructingWithEmptyErrorsList()
    {
        // Act
        var validationResult = new ValidationResult(new List<ValidationError>());

        // Assert
        Assert.True(validationResult.IsValid);
        Assert.Empty(validationResult.Errors);
    }

    [Fact]
    public void ShouldCreateValidResult_WhenUsingSuccess()
    {
        // Act
        var validationResult = ValidationResult.Success();

        // Assert
        Assert.True(validationResult.IsValid);
        Assert.Empty(validationResult.Errors);
    }

    [Fact]
    public void ShouldCreateResultWithSingleError_WhenUsingFailureWithPropertyNameAndMessage()
    {
        // Arrange
        var propertyName = "UserName";
        var errorMessage = "Username cannot be empty";

        // Act
        var validationResult = ValidationResult.Failure(propertyName, errorMessage);

        // Assert
        Assert.False(validationResult.IsValid);
        Assert.Single(validationResult.Errors);
        Assert.Equal(propertyName, validationResult.Errors[0].PropertyName);
        Assert.Equal(errorMessage, validationResult.Errors[0].ErrorMessage);
    }

    [Fact]
    public void ShouldCreateResultWithAllErrors_WhenUsingFailureWithMultipleErrors()
    {
        // Arrange
        var error1 = new ValidationError("Field1", "Error 1");
        var error2 = new ValidationError("Field2", "Error 2");
        var error3 = new ValidationError("Field3", "Error 3");

        // Act
        var validationResult = ValidationResult.Failure(error1, error2, error3);

        // Assert
        Assert.False(validationResult.IsValid);
        Assert.Equal(3, validationResult.Errors.Count);
        Assert.Contains(error1, validationResult.Errors);
        Assert.Contains(error2, validationResult.Errors);
        Assert.Contains(error3, validationResult.Errors);
    }

    [Fact]
    public void ShouldCreateValidResult_WhenUsingFailureWithNoErrors()
    {
        // Act
        var validationResult = ValidationResult.Failure();

        // Assert
        Assert.True(validationResult.IsValid);
        Assert.Empty(validationResult.Errors);
    }

    [Fact]
    public void ShouldAddErrorToCollection_WhenAddingErrorWithPropertyNameAndMessage()
    {
        // Arrange
        var validationResult = new ValidationResult();

        // Act
        validationResult.AddError("Password", "Password is too short");

        // Assert
        Assert.False(validationResult.IsValid);
        Assert.Single(validationResult.Errors);
        Assert.Equal("Password", validationResult.Errors[0].PropertyName);
        Assert.Equal("Password is too short", validationResult.Errors[0].ErrorMessage);
    }

    [Fact]
    public void ShouldAddErrorToCollection_WhenAddingErrorWithValidationErrorObject()
    {
        // Arrange
        var validationResult = new ValidationResult();
        var error = new ValidationError("Email", "Invalid email format", "INVALID_FORMAT");

        // Act
        validationResult.AddError(error);

        // Assert
        Assert.False(validationResult.IsValid);
        Assert.Single(validationResult.Errors);
        Assert.Equal(error, validationResult.Errors[0]);
    }

    [Fact]
    public void ShouldAddAllErrors_WhenAddingErrorWithMultipleErrors()
    {
        // Arrange
        var validationResult = new ValidationResult();

        // Act
        validationResult.AddError("Name", "Name is required");
        validationResult.AddError("Age", "Age must be positive");
        validationResult.AddError(new ValidationError("Email", "Email is invalid"));

        // Assert
        Assert.False(validationResult.IsValid);
        Assert.Equal(3, validationResult.Errors.Count);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsValidWithNoErrors()
    {
        // Arrange
        var validationResult = new ValidationResult();

        // Assert
        Assert.True(validationResult.IsValid);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsValidWithErrors()
    {
        // Arrange
        var validationResult = new ValidationResult(
            new ValidationError("Field", "Error message"));

        // Assert
        Assert.False(validationResult.IsValid);
    }

    [Fact]
    public void ShouldReturnReadOnlyCollection_WhenUsingErrors()
    {
        // Arrange
        var error = new ValidationError("Field", "Error");
        var validationResult = new ValidationResult(error);

        // Act
        var errors = validationResult.Errors;

        // Assert
        Assert.IsType<System.Collections.ObjectModel.ReadOnlyCollection<ValidationError>>(errors);
        Assert.Single(errors);
        Assert.Equal(error, errors[0]);
    }

    [Fact]
    public void ShouldMergeErrors_WhenCombiningWithAnotherValidationResult()
    {
        // Arrange
        var result1 = new ValidationResult(
            new ValidationError("Field1", "Error 1"));
        var result2 = new ValidationResult(
            new ValidationError("Field2", "Error 2"),
            new ValidationError("Field3", "Error 3"));

        // Act
        var combined = result1.Combine(result2);

        // Assert
        Assert.False(combined.IsValid);
        Assert.Equal(3, combined.Errors.Count);
        Assert.Contains(combined.Errors, e => e.PropertyName == "Field1");
        Assert.Contains(combined.Errors, e => e.PropertyName == "Field2");
        Assert.Contains(combined.Errors, e => e.PropertyName == "Field3");
    }

    [Fact]
    public void ShouldReturnResultWithOriginalErrors_WhenCombiningWithValidResult()
    {
        // Arrange
        var result1 = new ValidationResult(
            new ValidationError("Field1", "Error 1"));
        var result2 = ValidationResult.Success();

        // Act
        var combined = result1.Combine(result2);

        // Assert
        Assert.False(combined.IsValid);
        Assert.Single(combined.Errors);
        Assert.Equal("Field1", combined.Errors[0].PropertyName);
    }

    [Fact]
    public void ShouldReturnResultWithSecondErrors_WhenCombiningWithValidResultWithErrorResult()
    {
        // Arrange
        var result1 = ValidationResult.Success();
        var result2 = new ValidationResult(
            new ValidationError("Field2", "Error 2"));

        // Act
        var combined = result1.Combine(result2);

        // Assert
        Assert.False(combined.IsValid);
        Assert.Single(combined.Errors);
        Assert.Equal("Field2", combined.Errors[0].PropertyName);
    }

    [Fact]
    public void ShouldReturnValidResult_WhenCombiningTwoValidResults()
    {
        // Arrange
        var result1 = ValidationResult.Success();
        var result2 = ValidationResult.Success();

        // Act
        var combined = result1.Combine(result2);

        // Assert
        Assert.True(combined.IsValid);
        Assert.Empty(combined.Errors);
    }

    [Fact]
    public void ShouldNotModifyOriginalResults_WhenCombining()
    {
        // Arrange
        var result1 = new ValidationResult(
            new ValidationError("Field1", "Error 1"));
        var result2 = new ValidationResult(
            new ValidationError("Field2", "Error 2"));
        var originalCount1 = result1.Errors.Count;
        var originalCount2 = result2.Errors.Count;

        // Act
        var combined = result1.Combine(result2);

        // Assert
        Assert.Equal(originalCount1, result1.Errors.Count);
        Assert.Equal(originalCount2, result2.Errors.Count);
        Assert.Equal(2, combined.Errors.Count);
    }

    [Fact]
    public void ShouldReturnSuccessMessage_WhenCallingToStringWithValidResult()
    {
        // Arrange
        var validationResult = ValidationResult.Success();

        // Act
        var result = validationResult.ToString();

        // Assert
        Assert.Equal("Validation successful", result);
    }

    [Fact]
    public void ShouldReturnFormattedErrorMessage_WhenCallingToStringWithSingleError()
    {
        // Arrange
        var validationResult = ValidationResult.Failure("Name", "Name is required");

        // Act
        var result = validationResult.ToString();

        // Assert
        Assert.Contains("Validation failed with 1 error(s):", result);
        Assert.Contains("- Name: Name is required", result);
    }

    [Fact]
    public void ShouldReturnFormattedErrorMessages_WhenCallingToStringWithMultipleErrors()
    {
        // Arrange
        var validationResult = new ValidationResult(
            new ValidationError("Name", "Name is required"),
            new ValidationError("Email", "Invalid email format"),
            new ValidationError("Age", "Age must be positive"));

        // Act
        var result = validationResult.ToString();

        // Assert
        Assert.Contains("Validation failed with 3 error(s):", result);
        Assert.Contains("- Name: Name is required", result);
        Assert.Contains("- Email: Invalid email format", result);
        Assert.Contains("- Age: Age must be positive", result);
    }

    [Fact]
    public void ShouldFormatCorrectly_WhenCallingToStringWithErrorsContainingNewlines()
    {
        // Arrange
        var validationResult = new ValidationResult(
            new ValidationError("Field1", "Error with\nnewline"),
            new ValidationError("Field2", "Another error"));

        // Act
        var result = validationResult.ToString();

        // Assert
        Assert.Contains("Validation failed with 2 error(s):", result);
        Assert.Contains("- Field1: Error with\nnewline", result);
        Assert.Contains("- Field2: Another error", result);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(5, false)]
    public void ShouldReturnCorrectValue_WhenUsingIsValidWithDifferentErrorCounts(
        int errorCount, bool expectedValid)
    {
        // Arrange
        var validationResult = new ValidationResult();
        for (int i = 0; i < errorCount; i++)
        {
            validationResult.AddError($"Field{i}", $"Error {i}");
        }

        // Act & Assert
        Assert.Equal(expectedValid, validationResult.IsValid);
        Assert.Equal(errorCount, validationResult.Errors.Count);
    }

    [Fact]
    public void ShouldBehaveCorrectly_WhenUsingValidationResultInComplexScenario()
    {
        // Arrange
        var result1 = ValidationResult.Success();
        result1.AddError("Field1", "Error 1");

        var result2 = ValidationResult.Failure("Field2", "Error 2");
        result2.AddError(new ValidationError("Field3", "Error 3", "CODE3"));

        // Act
        var combined = result1.Combine(result2);
        combined.AddError("Field4", "Error 4");

        // Assert
        Assert.False(combined.IsValid);
        Assert.Equal(4, combined.Errors.Count);

        // Original results should remain unchanged
        Assert.Single(result1.Errors);
        Assert.Equal(2, result2.Errors.Count);

        // Combined result should have all errors
        var errorMessages = combined.Errors.Select(e => e.ErrorMessage).ToList();
        Assert.Contains("Error 1", errorMessages);
        Assert.Contains("Error 2", errorMessages);
        Assert.Contains("Error 3", errorMessages);
        Assert.Contains("Error 4", errorMessages);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullErrorsCollection()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ValidationResult((IEnumerable<ValidationError>)null!));

        Assert.Equal("collection", exception.ParamName);
    }

    [Fact]
    public void ShouldAcceptNull_WhenAddingErrorWithNullValidationError()
    {
        // Arrange
        var validationResult = new ValidationResult();

        // Act - The implementation allows null values to be added
        validationResult.AddError((ValidationError)null!);

        // Assert - The null error was added to the collection
        Assert.Single(validationResult.Errors);
        Assert.Null(validationResult.Errors[0]);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenCombiningWithNullValidationResult()
    {
        // Arrange
        var validationResult = new ValidationResult();

        // Act & Assert — Combine guards its argument (ArgumentNullException.ThrowIfNull)
        Assert.Throws<ArgumentNullException>(
            () => validationResult.Combine(null!));
    }
}
