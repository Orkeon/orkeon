using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Application.Tests.Ports;

/// <summary>R5: the durable memory key of an agent holds project, crew and agent.</summary>
public sealed class ScriptAgentMemoryKeyTests
{
    [Fact]
    [Trait("BR", "R5")]
    public void ShouldFormatKey_WhenProjectCrewAndAgentAreGiven()
    {
        var key = new ScriptAgentMemoryKey { Project = "proj", Crew = "crew", Agent = "agent1" };

        Assert.Equal("agent:proj:crew:agent1", key.ToString());
    }

    [Fact]
    [Trait("BR", "R5")]
    public void ShouldRoundTripKey_WhenCrewNameHoldsAColon()
    {
        var key = new ScriptAgentMemoryKey { Project = "proj", Crew = "a:b", Agent = "agent1" };

        var text = key.ToString();

        Assert.Equal("agent:proj:a%3Ab:agent1", text);
        Assert.Equal(key, ScriptAgentMemoryKey.Parse(text));
    }

    [Fact]
    [Trait("BR", "R5")]
    public void ShouldRefuseKey_WhenASegmentIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => ScriptAgentMemoryKey.Parse("agent:proj::agent1"));
        Assert.Throws<ArgumentException>(() => ScriptAgentMemoryKey.Parse("other:proj:crew:agent1"));
    }

    [Fact]
    [Trait("BR", "R5")]
    public void ShouldWriteNoneSegment_WhenCrewIsNull()
    {
        var key = new ScriptAgentMemoryKey { Project = "proj", Crew = null, Agent = "agent1" };

        var text = key.ToString();

        Assert.Equal("agent:proj:%none:agent1", text);
        Assert.Equal(key, ScriptAgentMemoryKey.Parse(text));
    }

    [Fact]
    [Trait("BR", "R5")]
    public void ShouldKeepCrewNamedNoneApart_WhenCrewIsLiterallyNone()
    {
        var key = new ScriptAgentMemoryKey { Project = "proj", Crew = "%none", Agent = "agent1" };
        var unnamed = new ScriptAgentMemoryKey { Project = "proj", Crew = null, Agent = "agent1" };

        var text = key.ToString();

        Assert.Equal("agent:proj:%25none:agent1", text);
        Assert.NotEqual(unnamed.ToString(), text);
        Assert.Equal("%none", ScriptAgentMemoryKey.Parse(text).Crew);
    }
}
