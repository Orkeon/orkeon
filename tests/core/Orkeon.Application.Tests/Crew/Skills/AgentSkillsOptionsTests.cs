using Orkeon.Application.Crew.Skills;

namespace Orkeon.Application.Tests.Crew.Skills;

/// <summary>The settings of Orkeon:Skills, judged at the host's start (#434).</summary>
public sealed class AgentSkillsOptionsTests
{
    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldFindNoProblem_WhenTheDefaultsStand()
    {
        // Arrange
        var options = new AgentSkillsOptions();

        // Act
        var problems = AgentSkillsOptions.Problems(options);

        // Assert
        Assert.Empty(problems);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldNameEachWrongKey_WhenTheLibraryIsRelativeAndTheCapsAreZero()
    {
        // Arrange
        var options = new AgentSkillsOptions { Library = ".orkeon/skills", MaxSkillChars = 0, MaxBlockChars = -1 };

        // Act
        var problems = AgentSkillsOptions.Problems(options);

        // Assert
        Assert.Collection(problems,
            p => Assert.StartsWith("Orkeon:Skills:Library", p, StringComparison.Ordinal),
            p => Assert.StartsWith("Orkeon:Skills:MaxSkillChars", p, StringComparison.Ordinal),
            p => Assert.StartsWith("Orkeon:Skills:MaxBlockChars", p, StringComparison.Ordinal));
    }

    [Theory]
    [Trait("BR", "CH-434")]
    [InlineData(100)]
    [InlineData(AgentSkillsOptions.MinBlockChars - 1)]
    public void ShouldNameTheLeastBlockCap_WhenTheBlockCannotHoldItsHeadingAndASkill(int maxBlockChars)
    {
        // Arrange
        var options = new AgentSkillsOptions { MaxBlockChars = maxBlockChars };

        // Act
        var problems = AgentSkillsOptions.Problems(options);

        // Assert
        var problem = Assert.Single(problems);
        Assert.StartsWith("Orkeon:Skills:MaxBlockChars", problem, StringComparison.Ordinal);
        Assert.Contains("500 or more", problem, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldFindNoProblem_WhenTheBlockCapIsTheLeastAccepted()
    {
        // Arrange
        var options = new AgentSkillsOptions { MaxBlockChars = AgentSkillsOptions.MinBlockChars };

        // Act
        var problems = AgentSkillsOptions.Problems(options);

        // Assert
        Assert.Empty(problems);
    }
}
