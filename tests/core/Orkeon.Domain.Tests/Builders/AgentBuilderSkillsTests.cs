using Orkeon.Domain.Agent;

namespace Orkeon.Domain.Tests.Builders;

/// <summary>#434: an agent's skills - carried as given, a blank path refused.</summary>
public sealed class AgentBuilderSkillsTests
{
    private static AgentBuilder MinimalAgent() =>
        new AgentBuilder().Role("Reviewer").Goal("Review the change");

    [Fact]
    [Trait("BR", "CH-434")]
    public void ShouldCarryTheSkills_WhenTheAgentDeclaresThem()
    {
        // Arrange
        var skills = new AgentSkills { Auto = true, Fallback = ["/workspace/skills/review.md"] };

        // Act
        var agent = MinimalAgent().Skills(skills).Build();

        // Assert
        Assert.Same(skills, agent.Skills);
    }

    [Theory]
    [Trait("BR", "CH-434")]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldRefuseTheSkills_WhenAPathIsBlank(bool inFallback)
    {
        // Arrange
        var builder = MinimalAgent();
        var skills = inFallback ? new AgentSkills { Auto = true, Fallback = [" "] } : new AgentSkills { Paths = [""] };

        // Act
        var refusal = Record.Exception(() => builder.Skills(skills));

        // Assert
        Assert.IsType<ArgumentException>(refusal);
    }
}
