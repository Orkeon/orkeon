using Orkeon.Domain.Task.ValueObjects;

namespace Orkeon.Domain.Tests.Common;

/// <summary>
/// Tests for TaskRequirement following Clean Architecture principles.
/// Tests the business rules and validation logic of the TaskRequirement record.
/// </summary>
public class TaskRequirementTests
{
    [Fact]
    public void ShouldCreateRequirement_WhenConstructingWithValidParameters()
    {
        // Arrange
        var name = "Database Access";
        var type = RequirementType.Permission;
        var description = "Requires database read/write permissions";
        var requiredSkills = new List<string> { "SQL", "Database Management" };
        var requiredTools = new List<string> { "SQL Server", "Entity Framework" };
        var constraints = new Dictionary<string, object>
        {
            { "maxConnections", 10 },
            { "timeout", "30s" }
        };

        // Act
        var requirement = TaskRequirement.Create(
            name,
            type,
            description,
            requiredSkills,
            requiredTools,
            constraints,
            true);

        // Assert
        Assert.Equal(name, requirement.Name);
        Assert.Equal(type, requirement.Type);
        Assert.Equal(description, requirement.Description);
        Assert.Equal(requiredSkills, requirement.RequiredSkills);
        Assert.Equal(requiredTools, requirement.RequiredTools);
        Assert.Equal(constraints, requirement.Constraints);
        Assert.True(requirement.IsMandatory);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullName()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => TaskRequirement.Create(
                null!,
                RequirementType.Skill,
                "Description"));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void ShouldThrowArgumentNullException_WhenConstructingWithNullDescription()
    {
        // Arrange & Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(
            () => TaskRequirement.Create(
                "Name",
                RequirementType.Skill,
                null!));

        Assert.Equal("description", exception.ParamName);
    }

    [Fact]
    public void ShouldCreateEmptyLists_WhenConstructingWithNullCollections()
    {
        // Act
        var requirement = TaskRequirement.Create(
            "Test Requirement",
            RequirementType.Skill,
            "Test description",
            requiredSkills: null,
            requiredTools: null,
            constraints: null);

        // Assert
        Assert.NotNull(requirement.RequiredSkills);
        Assert.Empty(requirement.RequiredSkills);
        Assert.NotNull(requirement.RequiredTools);
        Assert.Empty(requirement.RequiredTools);
        Assert.NotNull(requirement.Constraints);
        Assert.Empty(requirement.Constraints);
    }

    [Fact]
    public void ShouldBeTrueByDefault_WhenConstructingWithDefaultMandatory()
    {
        // Act
        var requirement = TaskRequirement.Create(
            "Default Requirement",
            RequirementType.Tool,
            "Default description");

        // Assert
        Assert.True(requirement.IsMandatory);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsSatisfiedByWithAllRequirementsMet()
    {
        // Arrange
        var requirement = TaskRequirement.Create(
            "Programming Task",
            RequirementType.Skill,
            "Requires programming skills",
            requiredSkills: ["C#", "JavaScript"],
            requiredTools: ["Visual Studio", "Git"]);

        var availableSkills = new List<string> { "C#", "JavaScript", "Python", "SQL" };
        var availableTools = new List<string> { "Visual Studio", "Git", "Docker", "Postman" };

        // Act
        var result = requirement.IsSatisfiedBy(availableSkills, availableTools);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsSatisfiedByWithMissingSkill()
    {
        // Arrange
        var requirement = TaskRequirement.Create(
            "Programming Task",
            RequirementType.Skill,
            "Requires programming skills",
            requiredSkills: ["C#", "JavaScript", "Python"],
            requiredTools: ["Visual Studio"]);

        var availableSkills = new List<string> { "C#", "JavaScript" }; // Missing Python
        var availableTools = new List<string> { "Visual Studio", "Git" };

        // Act
        var result = requirement.IsSatisfiedBy(availableSkills, availableTools);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldReturnFalse_WhenUsingIsSatisfiedByWithMissingTool()
    {
        // Arrange
        var requirement = TaskRequirement.Create(
            "Development Task",
            RequirementType.Tool,
            "Requires development tools",
            requiredSkills: ["C#"],
            requiredTools: ["Visual Studio", "Git", "Docker"]);

        var availableSkills = new List<string> { "C#", "JavaScript" };
        var availableTools = new List<string> { "Visual Studio", "Git" }; // Missing Docker

        // Act
        var result = requirement.IsSatisfiedBy(availableSkills, availableTools);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldReturnTrue_WhenUsingIsSatisfiedByWithNoRequirements()
    {
        // Arrange
        var requirement = TaskRequirement.Create(
            "Simple Task",
            RequirementType.Custom,
            "No specific requirements");

        var availableSkills = new List<string> { "C#" };
        var availableTools = new List<string> { "Visual Studio" };

        // Act
        var result = requirement.IsSatisfiedBy(availableSkills, availableTools);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ShouldReturnFalseIfRequirementsExist_WhenUsingIsSatisfiedByWithEmptyAvailableCollections()
    {
        // Arrange
        var requirement = TaskRequirement.Create(
            "Task with Requirements",
            RequirementType.Skill,
            "Requires skills",
            requiredSkills: ["C#"]);

        var availableSkills = new List<string>();
        var availableTools = new List<string>();

        // Act
        var result = requirement.IsSatisfiedBy(availableSkills, availableTools);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ShouldHaveExpectedValues_WhenUsingRequirementType()
    {
        // Act & Assert — sealed records use string values; verify All contains expected members
        Assert.Equal(6, RequirementType.All.Count);
        Assert.Contains(RequirementType.Skill, RequirementType.All);
        Assert.Contains(RequirementType.Tool, RequirementType.All);
        Assert.Contains(RequirementType.Resource, RequirementType.All);
        Assert.Contains(RequirementType.Permission, RequirementType.All);
        Assert.Contains(RequirementType.Capability, RequirementType.All);
        Assert.Contains(RequirementType.Custom, RequirementType.All);
    }

    [Fact]
    public void ShouldReturnCorrectName_WhenUsingRequirementTypeToString()
    {
        // Act & Assert
        Assert.Equal("Skill", RequirementType.Skill.ToString());
        Assert.Equal("Tool", RequirementType.Tool.ToString());
        Assert.Equal("Resource", RequirementType.Resource.ToString());
        Assert.Equal("Permission", RequirementType.Permission.ToString());
        Assert.Equal("Capability", RequirementType.Capability.ToString());
        Assert.Equal("Custom", RequirementType.Custom.ToString());
    }

    [Fact]
    public void ShouldReturnTrue_WhenComparingEqualityWithSameValues()
    {
        // Arrange
        var skills = new List<string> { "C#" };
        var tools = new List<string> { "VS" };
        var constraints = new Dictionary<string, object> { { "key", "value" } };

        var requirement1 = TaskRequirement.Create(
            "Test",
            RequirementType.Skill,
            "Description",
            skills,
            tools,
            constraints,
            true);

        var requirement2 = TaskRequirement.Create(
            "Test",
            RequirementType.Skill,
            "Description",
            skills,
            tools,
            constraints,
            true);

        // Act & Assert
        Assert.Equal(requirement1, requirement2);
        Assert.True(requirement1.Equals(requirement2));
        Assert.Equal(requirement1.GetHashCode(), requirement2.GetHashCode());
    }

    [Fact]
    public void ShouldReturnFalse_WhenComparingEqualityWithDifferentValues()
    {
        // Arrange
        var requirement1 = TaskRequirement.Create(
            "Test1",
            RequirementType.Skill,
            "Description1");

        var requirement2 = TaskRequirement.Create(
            "Test2",
            RequirementType.Tool,
            "Description2");

        // Act & Assert
        Assert.NotEqual(requirement1, requirement2);
        Assert.False(requirement1.Equals(requirement2));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShouldAcceptBothValues_WhenUsingIsMandatory(bool isMandatory)
    {
        // Act
        var requirement = TaskRequirement.Create(
            "Test",
            RequirementType.Custom,
            "Test description",
            isMandatory: isMandatory);

        // Assert
        Assert.Equal(isMandatory, requirement.IsMandatory);
    }

    [Fact]
    public void ShouldBeImmutable_WhenUsingRequiredCollections()
    {
        // Arrange
        var skills = new List<string> { "Existing Skill" };
        var tools = new List<string> { "Existing Tool" };
        var constraints = new Dictionary<string, object> { { "key", "value" } };

        var requirement = TaskRequirement.Create(
            "Immutable Test",
            RequirementType.Skill,
            "Test immutable collections",
            skills,
            tools,
            constraints);

        // Act - Modify the original collections
        skills.Add("New Skill");
        tools.Add("New Tool");
        constraints.Add("newConstraint", "newValue");

        // Assert - Requirement collections should not be affected
        Assert.Single(requirement.RequiredSkills);
        Assert.Contains("Existing Skill", requirement.RequiredSkills);
        Assert.Single(requirement.RequiredTools);
        Assert.Contains("Existing Tool", requirement.RequiredTools);
        Assert.Single(requirement.Constraints);
        Assert.Equal("value", requirement.Constraints["key"]);
    }

    [Fact]
    public void ShouldBeExact_WhenUsingIsSatisfiedByWithCaseSensitiveComparison()
    {
        // Arrange
        var requirement = TaskRequirement.Create(
            "Case Sensitive Test",
            RequirementType.Skill,
            "Tests case sensitivity",
            requiredSkills: ["CSharp"]);

        var availableSkills = new List<string> { "csharp" }; // Different case
        var availableTools = new List<string>();

        // Act
        var result = requirement.IsSatisfiedBy(availableSkills, availableTools);

        // Assert
        Assert.False(result); // Should be case sensitive
    }
}
