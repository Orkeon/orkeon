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
}
