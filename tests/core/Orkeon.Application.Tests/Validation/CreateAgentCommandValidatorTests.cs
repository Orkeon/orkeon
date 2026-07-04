using Orkeon.Application.Agent.Commands.CreateAgent;
using static Orkeon.Tests.Shared.Constants.TestAgentConstants;

namespace Orkeon.Application.Tests.Validation;

public class CreateAgentCommandValidatorTests
{
    private readonly CreateAgentCommandValidator _validator = new();

    [Fact]
    public void ShouldPass_WhenCommandHasRoleAndGoal()
    {
        // Arrange
        var command = new CreateAgentCommand(RoleDeveloper, GoalWriteCode, null, null);

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ShouldFail_WhenRoleIsNull()
    {
        // Arrange
        var command = new CreateAgentCommand(null!, GoalWriteCode, null, null);

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Role"));
    }

    [Fact]
    public void ShouldFail_WhenRoleIsEmpty()
    {
        // Arrange
        var command = new CreateAgentCommand("", GoalWriteCode, null, null);

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Role"));
    }

    [Fact]
    public void ShouldFail_WhenRoleIsWhitespace()
    {
        // Arrange
        var command = new CreateAgentCommand("   ", GoalWriteCode, null, null);

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Role"));
    }

    [Fact]
    public void ShouldFail_WhenGoalIsNull()
    {
        // Arrange
        var command = new CreateAgentCommand(RoleDeveloper, null!, null, null);

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Goal"));
    }

    [Fact]
    public void ShouldFail_WhenGoalIsEmpty()
    {
        // Arrange
        var command = new CreateAgentCommand(RoleDeveloper, "", null, null);

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Goal"));
    }

    [Fact]
    public void ShouldFail_WhenGoalIsWhitespace()
    {
        // Arrange
        var command = new CreateAgentCommand(RoleDeveloper, "   ", null, null);

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Goal"));
    }

    [Fact]
    public void ShouldReturnBothErrors_WhenRoleAndGoalAreMissing()
    {
        // Arrange
        var command = new CreateAgentCommand("", "", null, null);

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(2, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Contains("Role"));
        Assert.Contains(result.Errors, e => e.Contains("Goal"));
    }

    [Fact]
    public void ShouldPass_WhenOptionalFieldsAreNull()
    {
        // Arrange
        var command = new CreateAgentCommand(RoleDeveloper, GoalWriteCode, null, null);

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void ShouldPass_WhenAllFieldsAreProvided()
    {
        // Arrange
        var command = new CreateAgentCommand(
            RoleDeveloper,
            GoalWriteCode,
            "Experienced developer",
            ["code_tool"]);

        // Act
        var result = _validator.Validate(command);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }
}
