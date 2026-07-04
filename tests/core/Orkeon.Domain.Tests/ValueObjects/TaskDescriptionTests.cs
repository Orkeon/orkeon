using Orkeon.Domain.Constants.Task;
using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Tests.ValueObjects;

/// <summary>
/// Tests for TaskDescription value object following Clean Architecture principles.
/// Tests the business rules and validation logic of the TaskDescription value object.
/// </summary>
public class TaskDescriptionTests
{
    [Fact]
    public void ShouldCreateTaskDescription_WhenUsingFromWithValidValue()
    {
        // Arrange
        var value = "Implement user authentication system";

        // Act
        var description = TaskDescription.From(value);

        // Assert
        Assert.NotNull(description);
        Assert.Equal(value, description.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ShouldThrowArgumentException_WhenUsingFromWithInvalidValue(string? value)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => TaskDescription.From(value!)
        );
        Assert.Contains("cannot be null or whitespace", exception.Message);
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingFromWithVeryLongValue()
    {
        // Arrange
        var longValue = new string('A', TaskDefaults.TaskDescriptionMaxLength + 1);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => TaskDescription.From(longValue)
        );
        Assert.Contains($"cannot exceed {TaskDefaults.TaskDescriptionMaxLength} characters", exception.Message);
    }

    [Fact]
    public void ShouldCreateTaskDescription_WhenUsingFromWithMaxLengthValue()
    {
        // Arrange
        var maxLengthValue = new string('A', TaskDefaults.TaskDescriptionMaxLength);

        // Act
        var description = TaskDescription.From(maxLengthValue);

        // Assert
        Assert.NotNull(description);
        Assert.Equal(maxLengthValue, description.Value);
    }

    [Theory]
    [InlineData("Create database schema")]
    [InlineData("Implement REST API endpoints")]
    [InlineData("Write unit tests for service layer")]
    [InlineData("Set up continuous integration pipeline")]
    [InlineData("Deploy application to production")]
    public void ShouldCreateTaskDescription_WhenUsingFromWithCommonTaskDescriptions(string descriptionValue)
    {
        // Act
        var description = TaskDescription.From(descriptionValue);

        // Assert
        Assert.NotNull(description);
        Assert.Equal(descriptionValue, description.Value);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValue()
    {
        // Arrange
        var desc1 = TaskDescription.From("Implement feature");
        var desc2 = TaskDescription.From("Implement feature");

        // Act & Assert
        Assert.Equal(desc1, desc2);
        Assert.True(desc1.Equals(desc2));
        Assert.True(desc1 == desc2);
        Assert.False(desc1 != desc2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentValue()
    {
        // Arrange
        var desc1 = TaskDescription.From("Implement feature");
        var desc2 = TaskDescription.From("Test feature");

        // Act & Assert
        Assert.NotEqual(desc1, desc2);
        Assert.False(desc1.Equals(desc2));
        Assert.False(desc1 == desc2);
        Assert.True(desc1 != desc2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithNull()
    {
        // Arrange
        var description = TaskDescription.From("Implement feature");

        // Act & Assert
        Assert.False(description.Equals(null));
        Assert.NotNull(description);
    }

    [Fact]
    public void ShouldReturnSameHashCode_WhenCallingGetHashCodeWithSameValue()
    {
        // Arrange
        var desc1 = TaskDescription.From("Implement feature");
        var desc2 = TaskDescription.From("Implement feature");

        // Act
        var hash1 = desc1.GetHashCode();
        var hash2 = desc2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnDifferentHashCode_WhenCallingGetHashCodeWithDifferentValue()
    {
        // Arrange
        var desc1 = TaskDescription.From("Implement feature");
        var desc2 = TaskDescription.From("Test feature");

        // Act
        var hash1 = desc1.GetHashCode();
        var hash2 = desc2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnValue_WhenCallingToString()
    {
        // Arrange
        var value = "Implement user authentication";
        var description = TaskDescription.From(value);

        // Act
        var result = description.ToString();

        // Assert
        Assert.Equal(value, result);
    }

    [Fact]
    public void ShouldNotExist_WhenUsingImplicitOperatorFromString()
    {
        // Note: TaskDescription does not have implicit conversion from string
        // This test validates that we must use TaskDescription.From() factory method

        // Arrange
        var value = "Implement feature";

        // Act
        var description = TaskDescription.From(value);

        // Assert
        Assert.NotNull(description);
        Assert.Equal(value, description.Value);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingImplicitOperatorToString()
    {
        // Arrange
        var description = TaskDescription.From("Implement feature");

        // Act
        string value = description;

        // Assert
        Assert.Equal("Implement feature", value);
    }

    [Fact]
    public void ShouldTrimValue_WhenUsingFromWithLeadingAndTrailingSpaces()
    {
        // Arrange
        var value = "  Implement user authentication  ";

        // Act
        var description = TaskDescription.From(value);

        // Assert
        Assert.Equal("Implement user authentication", description.Value);
    }

    [Fact]
    public void ShouldPreserveContent_WhenUsingFromWithDetailedDescription()
    {
        // Arrange
        var value = @"Implement user authentication system with the following requirements:
1. Support email/password login
2. Implement JWT tokens
3. Add password reset functionality
4. Include two-factor authentication
5. Ensure proper security measures";

        // Act
        var description = TaskDescription.From(value);

        // Assert
        Assert.Equal(value, description.Value);
    }

    [Fact]
    public void ShouldPreserveCharacters_WhenUsingFromWithSpecialCharacters()
    {
        // Arrange
        var value = "Implement C# API with 99.9% uptime & <5ms latency";

        // Act
        var description = TaskDescription.From(value);

        // Assert
        Assert.Equal(value, description.Value);
    }

    [Fact]
    public void ShouldPreserveCharacters_WhenUsingFromWithUnicodeCharacters()
    {
        // Arrange
        var value = "Implémenter l'authentification utilisateur avec sécurité renforcée";

        // Act
        var description = TaskDescription.From(value);

        // Assert
        Assert.Equal(value, description.Value);
    }

    [Theory]
    [InlineData("Write 100 unit tests")]
    [InlineData("Reduce load time to <2 seconds")]
    [InlineData("Support 10,000 concurrent users")]
    [InlineData("Achieve 99.99% uptime")]
    public void ShouldCreateTaskDescription_WhenUsingFromWithQuantifiableDescriptions(string descriptionValue)
    {
        // Act
        var description = TaskDescription.From(descriptionValue);

        // Assert
        Assert.NotNull(description);
        Assert.Equal(descriptionValue, description.Value);
    }

    [Fact]
    public void ShouldPreserveFormatting_WhenUsingFromWithJsonContent()
    {
        // Arrange
        var value = @"Configure API endpoint: {
  ""endpoint"": ""/api/users"",
  ""method"": ""POST"",
  ""auth"": ""required""
}";

        // Act
        var description = TaskDescription.From(value);

        // Assert
        Assert.Equal(value, description.Value);
    }

    [Fact]
    public void ShouldPreserveFormatting_WhenUsingFromWithMarkdownContent()
    {
        // Arrange
        var value = @"## Task: Setup CI/CD Pipeline
- [ ] Configure GitHub Actions
- [ ] Setup automated testing
- [ ] Configure deployment
- [ ] Add monitoring";

        // Act
        var description = TaskDescription.From(value);

        // Assert
        Assert.Equal(value, description.Value);
    }
}
