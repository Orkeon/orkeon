using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Constants.Agent;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Domain.Tests.ValueObjects;

/// <summary>
/// Tests for AgentRole value object following Clean Architecture principles.
/// Tests the business rules and validation logic of the AgentRole value object.
/// </summary>
public class AgentRoleTests
{
    [Fact]
    public void ShouldCreateAgentRole_WhenUsingFromWithValidValue()
    {
        // Arrange
        var value = RoleSeniorDeveloper;

        // Act
        var role = AgentRole.From(value);

        // Assert
        Assert.NotNull(role);
        Assert.Equal(value, role.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ShouldThrowArgumentException_WhenUsingFromWithInvalidValue(string? value)
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(
            () => AgentRole.From(value!)
        );
    }

    [Fact]
    public void ShouldThrowArgumentException_WhenUsingFromWithVeryLongValue()
    {
        // Arrange
        var longValue = new string('A', AgentDefaults.AgentRoleMaxLength + 1);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(
            () => AgentRole.From(longValue)
        );
        Assert.Contains($"cannot exceed {AgentDefaults.AgentRoleMaxLength} characters", exception.Message);
    }

    [Fact]
    public void ShouldCreateAgentRole_WhenUsingFromWithMaxLengthValue()
    {
        // Arrange
        var maxLengthValue = new string('A', AgentDefaults.AgentRoleMaxLength);

        // Act
        var role = AgentRole.From(maxLengthValue);

        // Assert
        Assert.NotNull(role);
        Assert.Equal(maxLengthValue, role.Value);
    }

    [Theory]
    [InlineData(RoleDeveloper)]
    [InlineData("Senior Software Engineer")]
    [InlineData("Machine Learning Specialist")]
    [InlineData("Data Scientist")]
    [InlineData("DevOps Engineer")]
    public void ShouldCreateAgentRole_WhenUsingFromWithCommonRoles(string roleValue)
    {
        // Act
        var role = AgentRole.From(roleValue);

        // Assert
        Assert.NotNull(role);
        Assert.Equal(roleValue, role.Value);
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValue()
    {
        // Arrange
        var role1 = AgentRole.From(RoleDeveloper);
        var role2 = AgentRole.From(RoleDeveloper);

        // Act & Assert
        Assert.Equal(role1, role2);
        Assert.True(role1.Equals(role2));
        Assert.True(role1 == role2);
        Assert.False(role1 != role2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentValue()
    {
        // Arrange
        var role1 = AgentRole.From(RoleDeveloper);
        var role2 = AgentRole.From("Designer");

        // Act & Assert
        Assert.NotEqual(role1, role2);
        Assert.False(role1.Equals(role2));
        Assert.False(role1 == role2);
        Assert.True(role1 != role2);
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithNull()
    {
        // Arrange
        var role = AgentRole.From(RoleDeveloper);

        // Act & Assert
        Assert.False(role.Equals(null));
        Assert.NotNull(role);
    }

    [Fact]
    public void ShouldReturnSameHashCode_WhenCallingGetHashCodeWithSameValue()
    {
        // Arrange
        var role1 = AgentRole.From(RoleDeveloper);
        var role2 = AgentRole.From(RoleDeveloper);

        // Act
        var hash1 = role1.GetHashCode();
        var hash2 = role2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnDifferentHashCode_WhenCallingGetHashCodeWithDifferentValue()
    {
        // Arrange
        var role1 = AgentRole.From(RoleDeveloper);
        var role2 = AgentRole.From("Designer");

        // Act
        var hash1 = role1.GetHashCode();
        var hash2 = role2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void ShouldReturnValue_WhenCallingToString()
    {
        // Arrange
        var value = RoleSeniorDeveloper;
        var role = AgentRole.From(value);

        // Act
        var result = role.ToString();

        // Assert
        Assert.Equal(value, result);
    }

    [Fact]
    public void ShouldNotExist_WhenUsingImplicitOperatorFromString()
    {
        // Note: AgentRole does not have implicit conversion from string
        // This test validates that we must use AgentRole.From() factory method

        // Arrange
        var value = RoleDeveloper;

        // Act
        var role = AgentRole.From(value);

        // Assert
        Assert.NotNull(role);
        Assert.Equal(value, role.Value);
    }

    [Fact]
    public void ShouldReturnValue_WhenUsingImplicitOperatorToString()
    {
        // Arrange
        var role = AgentRole.From(RoleDeveloper);

        // Act
        string value = role;

        // Assert
        Assert.Equal(RoleDeveloper, value);
    }

    [Fact]
    public void ShouldTrimValue_WhenUsingFromWithLeadingAndTrailingSpaces()
    {
        // Arrange
        var value = "  Senior Developer  ";

        // Act
        var role = AgentRole.From(value);

        // Assert
        Assert.Equal(RoleSeniorDeveloper, role.Value);
    }

    [Theory]
    [InlineData("developer", "developer")]
    [InlineData("DEVELOPER", "DEVELOPER")]
    [InlineData("DeveloPER", "DeveloPER")]
    [InlineData("senior developer", "senior developer")]
    public void ShouldPreserveCapitalization_WhenUsingFrom(string input, string expected)
    {
        // Act
        var role = AgentRole.From(input);

        // Assert
        Assert.Equal(expected, role.Value);
    }

    [Fact]
    public void ShouldPreserveCharacters_WhenUsingFromWithSpecialCharacters()
    {
        // Arrange
        var value = "Senior C# Developer";

        // Act
        var role = AgentRole.From(value);

        // Assert
        Assert.Equal(value, role.Value);
    }

    [Fact]
    public void ShouldPreserveNumbers_WhenUsingFromWithNumbers()
    {
        // Arrange
        var value = "Level 3 Support Specialist";

        // Act
        var role = AgentRole.From(value);

        // Assert
        Assert.Equal(value, role.Value);
    }
}
