using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Constants.Agent;

namespace Orkeon.Domain.Tests.ValueObjects;

/// <summary>
/// Tests for AgentGoal value object following Clean Architecture principles.
/// Tests the business rules and validation logic of the AgentGoal value object.
/// </summary>
public class AgentGoalTests
{
    [Fact]
    public void ShouldCreateAgentGoal_WhenUsingFromWithValidValue()
    {
        // Arrange
        var value = "Build high-quality software solutions";

        // Act
        var goal = AgentGoal.From(value);

        // Assert
        Assert.NotNull(goal);
        Assert.Equal(value, goal.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ShouldThrowArgumentException_WhenUsingFromWithInvalidValue(string? value)
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(
            () => AgentGoal.From(value!)
        );
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingFromWithVeryLongValue()
    {
        // Arrange
        var longValue = new string('A', AgentDefaults.AgentGoalMaxLength + 1);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => AgentGoal.From(longValue)
        );
        Assert.Contains($"cannot exceed {AgentDefaults.AgentGoalMaxLength} characters", exception.Message);
    }

    [Fact]
    public void ShouldCreateAgentGoal_WhenUsingFromWithMaxLengthValue()
    {
        // Arrange
        var maxLengthValue = new string('A', AgentDefaults.AgentGoalMaxLength);

        // Act
        var goal = AgentGoal.From(maxLengthValue);

        // Assert
        Assert.NotNull(goal);
        Assert.Equal(maxLengthValue, goal.Value);
    }

    [Theory]
    [InlineData("Develop efficient algorithms")]
    [InlineData("Create user-friendly interfaces")]
    [InlineData("Optimize system performance")]
    [InlineData("Ensure code quality and maintainability")]
    [InlineData("Implement robust security measures")]
    public void ShouldCreateAgentGoal_WhenUsingFromWithCommonGoals(string goalValue)
    {
        // Act
        var goal = AgentGoal.From(goalValue);

        // Assert
        Assert.NotNull(goal);
        Assert.Equal(goalValue, goal.Value);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValue()
    {
        // Arrange
        var goal1 = AgentGoal.From("Build software");
        var goal2 = AgentGoal.From("Build software");

        // Act & Assert
        Assert.Equal(goal1, goal2);
        Assert.True(goal1.Equals(goal2));
        Assert.True(goal1 == goal2);
        Assert.False(goal1 != goal2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentValue()
    {
        // Arrange
        var goal1 = AgentGoal.From("Build software");
        var goal2 = AgentGoal.From("Test software");

        // Act & Assert
        Assert.NotEqual(goal1, goal2);
        Assert.False(goal1.Equals(goal2));
        Assert.False(goal1 == goal2);
        Assert.True(goal1 != goal2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithNull()
    {
        // Arrange
        var goal = AgentGoal.From("Build software");

        // Act & Assert
        Assert.False(goal.Equals(null));
        Assert.NotNull(goal);
    }

    [Fact]
    public void ShouldReturnSameHashCode_WhenCallingGetHashCodeWithSameValue()
    {
        // Arrange
        var goal1 = AgentGoal.From("Build software");
        var goal2 = AgentGoal.From("Build software");

        // Act
        var hash1 = goal1.GetHashCode();
        var hash2 = goal2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnDifferentHashCode_WhenCallingGetHashCodeWithDifferentValue()
    {
        // Arrange
        var goal1 = AgentGoal.From("Build software");
        var goal2 = AgentGoal.From("Test software");

        // Act
        var hash1 = goal1.GetHashCode();
        var hash2 = goal2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnValue_WhenCallingToString()
    {
        // Arrange
        var value = "Build high-quality software";
        var goal = AgentGoal.From(value);

        // Act
        var result = goal.ToString();

        // Assert
        Assert.Equal(value, result);
    }

    [Fact]
    public void ShouldNotExist_WhenUsingImplicitOperatorFromString()
    {
        // Note: AgentGoal does not have implicit conversion from string
        // This test validates that we must use AgentGoal.From() factory method

        // Arrange
        var value = "Build software";

        // Act
        var goal = AgentGoal.From(value);

        // Assert
        Assert.NotNull(goal);
        Assert.Equal(value, goal.Value);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingImplicitOperatorToString()
    {
        // Arrange
        var goal = AgentGoal.From("Build software");

        // Act
        string value = goal;

        // Assert
        Assert.Equal("Build software", value);
    }

    [Fact]
    public void ShouldTrimValue_WhenUsingFromWithLeadingAndTrailingSpaces()
    {
        // Arrange
        var value = "  Build high-quality software  ";

        // Act
        var goal = AgentGoal.From(value);

        // Assert
        Assert.Equal("Build high-quality software", goal.Value);
    }

    [Fact]
    public void ShouldPreserveContent_WhenUsingFromWithMultilineGoal()
    {
        // Arrange
        var value = "Build software that:\n- Is maintainable\n- Is scalable\n- Is secure";

        // Act
        var goal = AgentGoal.From(value);

        // Assert
        Assert.Equal(value, goal.Value);
    }

    [Fact]
    public void ShouldPreserveCharacters_WhenUsingFromWithSpecialCharacters()
    {
        // Arrange
        var value = "Build C# applications with 99.9% uptime";

        // Act
        var goal = AgentGoal.From(value);

        // Assert
        Assert.Equal(value, goal.Value);
    }

    [Fact]
    public void ShouldPreserveCharacters_WhenUsingFromWithUnicodeCharacters()
    {
        // Arrange
        var value = "Créer des applications françaises de qualité";

        // Act
        var goal = AgentGoal.From(value);

        // Assert
        Assert.Equal(value, goal.Value);
    }

    [Theory]
    [InlineData("Optimize performance by 50%")]
    [InlineData("Reduce bugs to less than 1%")]
    [InlineData("Achieve 100% code coverage")]
    [InlineData("Deploy every 2 weeks")]
    public void ShouldCreateAgentGoal_WhenUsingFromWithQuantifiableGoals(string goalValue)
    {
        // Act
        var goal = AgentGoal.From(goalValue);

        // Assert
        Assert.NotNull(goal);
        Assert.Equal(goalValue, goal.Value);
    }
}
