using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>#439 — <c>thinking: { effort: auto }</c> loads as a level left to System One, and exports as written.</summary>
public sealed class LlmThinkingAutoYamlTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static YamlCrewDefinitionLoader Loader() =>
        new(new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

    private static string Crew(string agentThinking, string taskOverride = "") => $$"""
name: c
goal: g
agents:
  a:
    role: R
    goal: G
    llm:
      thinking: {{agentThinking}}
tasks:
  t:
    description: D
    expectedOutput: O
    agent: a
{{taskOverride}}
""";

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldLeaveTheLevelToSystemOne_WhenTheAgentWritesEffortAuto()
    {
        // Act
        var config = await Loader().LoadFromStringAsync(Crew("{ enabled: true, effort: Auto }"), Ct);

        // Assert
        Assert.Equal(new LlmThinkingConfig { Enabled = true, AutoEffort = LlmAutoEffort.Any }, config.Agents.Single().LlmConfig!.Thinking);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldLeaveTheLevelToSystemOne_WhenATaskOverrideWritesEffortAuto()
    {
        // Act
        var config = await Loader().LoadFromStringAsync(
            Crew("{ effort: low }", "    llm_override:\n      thinking: { effort: auto }"), Ct);

        // Assert
        Assert.True(config.Tasks.Single().LlmOverride!.Thinking!.HasAuto);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldExportTheMarkerAsWritten_WhenTheAgentLeavesTheLevelToSystemOne()
    {
        // Arrange
        var fs = new FakeFileSystemService();
        var serializer = new YamlDotNetSerializer();
        var loader = new YamlCrewDefinitionLoader(serializer, fs, NullLogger<YamlCrewDefinitionLoader>.Instance);
        var exporter = new YamlCrewExporter(serializer, fs, NullLogger<YamlCrewExporter>.Instance);
        var config = await loader.LoadFromStringAsync(Crew("{ effort: auto }"), Ct);

        // Act
        var yaml = exporter.ExportToString(config);

        // Assert
        Assert.Contains("effort: auto", yaml, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldBoundThePick_WhenTheAgentWritesTheMapForm()
    {
        // Act
        var config = await Loader().LoadFromStringAsync(Crew("{ effort: { auto: true, min: low, max: high } }"), Ct);

        // Assert
        Assert.Equal(LlmAutoEffort.Bounded("low", "high"), config.Agents.Single().LlmConfig!.Thinking!.AutoEffort);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldLeaveTheSwitchToSystemOne_WhenTheAgentWritesEnabledAuto()
    {
        // Act
        var config = await Loader().LoadFromStringAsync(Crew("{ enabled: auto, effort: high }"), Ct);

        // Assert
        Assert.Equal(new LlmThinkingConfig { AutoEnabled = true, Effort = "high" }, config.Agents.Single().LlmConfig!.Thinking);
    }

    [Theory]
    [Trait("BR", "CH-439")]
    [InlineData("{ effort: { auto: false, max: high } }", "needs 'auto: true'")]
    [InlineData("{ effort: { auto: true, top: high } }", "unknown key 'top'")]
    [InlineData("{ effort: { auto: yes please } }", "effort.auto: 'yes please' is not true or false")]
    [InlineData("{ effort: { auto: true, min: huge } }", "effort.min: 'huge' is not a thinking level")]
    [InlineData("{ effort: { auto: true, min: high, max: low } }", "min is at most max")]
    [InlineData("{ enabled: maybe }", "enabled: 'maybe' is not true, false or auto")]
    public async System.Threading.Tasks.Task ShouldRefuseTheLoadNamingTheAgent_WhenTheThinkingIsMalformed(string thinking, string expected)
    {
        // Act
        var error = await Record.ExceptionAsync(() => Loader().LoadFromStringAsync(Crew(thinking), Ct));

        // Assert
        Assert.NotNull(error);
        Assert.Contains("Agent 'a' llm.thinking", error.ToString(), StringComparison.Ordinal);
        Assert.Contains(expected, error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("BR", "CH-439")]
    [InlineData("{ effort: { auto: true, min: low, max: high } }", "max: high")]
    [InlineData("{ enabled: auto, effort: high }", "enabled: auto")]
    public async System.Threading.Tasks.Task ShouldExportWhatIsLeftToSystemOneAsWritten_WhenTheAgentBoundsThePickOrTheSwitch(string thinking, string expected)
    {
        // Arrange
        var fs = new FakeFileSystemService();
        var serializer = new YamlDotNetSerializer();
        var loader = new YamlCrewDefinitionLoader(serializer, fs, NullLogger<YamlCrewDefinitionLoader>.Instance);
        var exporter = new YamlCrewExporter(serializer, fs, NullLogger<YamlCrewExporter>.Instance);
        var config = await loader.LoadFromStringAsync(Crew(thinking), Ct);

        // Act
        var yaml = exporter.ExportToString(config);

        // Assert
        var reloaded = await loader.LoadFromStringAsync(yaml, Ct);
        Assert.Contains(expected, yaml, StringComparison.Ordinal);
        Assert.Equal(config.Agents.Single().LlmConfig!.Thinking, reloaded.Agents.Single().LlmConfig!.Thinking);
    }

    [Theory]
    [Trait("BR", "CH-439")]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("y", true)]
    [InlineData("True", true)]
    [InlineData("'YES'", true)]
    [InlineData("no", false)]
    [InlineData("off", false)]
    [InlineData("n", false)]
    [InlineData("FALSE", false)]
    public async System.Threading.Tasks.Task ShouldLoadTheSwitchAsBefore_WhenTheAgentWritesAYamlBooleanSpelling(string written, bool expected)
    {
        // Act
        var config = await Loader().LoadFromStringAsync(Crew($"{{ enabled: {written} }}"), Ct);

        // Assert
        Assert.Equal(new LlmThinkingConfig { Enabled = expected }, config.Agents.Single().LlmConfig!.Thinking);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldLeaveNothingToSystemOne_WhenTheAgentSwitchesThinkingOffUnderACrewLevelEffortAuto()
    {
        // Arrange
        const string yaml = """
name: c
goal: g
llm:
  thinking: { effort: auto }
agents:
  a:
    role: R
    goal: G
    llm:
      thinking: { enabled: false }
tasks:
  t:
    description: D
    expectedOutput: O
    agent: a
""";

        // Act
        var config = await Loader().LoadFromStringAsync(yaml, Ct);

        // Assert
        var thinking = config.Agents.Single().LlmConfig!.Thinking!;
        Assert.False(thinking.HasAuto);
        Assert.False(thinking.Enabled);
    }

    [Theory]
    [Trait("BR", "CH-439")]
    [InlineData("{ effort: High }")]
    [InlineData("{ effort: { Auto: YES, MIN: Low, Max: HIGH } }")]
    [InlineData("{ effort: { auto: on, min: low, max: high } }")]
    public async System.Threading.Tasks.Task ShouldReadLevelsKeysAndBooleansInAnyCase_WhenTheThinkingIsWrittenSo(string thinking)
    {
        // Act
        var config = await Loader().LoadFromStringAsync(Crew(thinking), Ct);

        // Assert: as auto and enabled: are read.
        var loaded = config.Agents.Single().LlmConfig!.Thinking!;
        if (loaded.AutoEffort is { } auto)
            Assert.Equal(LlmAutoEffort.Bounded("low", "high"), auto);
        else
            Assert.Equal("high", loaded.Effort);
    }

    [Theory]
    [Trait("BR", "CH-439")]
    [InlineData("{ enabled: [true] }", "enabled: '[true]' is not true, false or auto")]
    [InlineData("{ effort: 5 }", "'auto' (System One picks the level), or { auto: true, min, max }")]
    public async System.Threading.Tasks.Task ShouldQuoteTheValueAndNameEveryForm_WhenTheThinkingIsMalformed(string thinking, string expected)
    {
        // Act
        var error = await Record.ExceptionAsync(() => Loader().LoadFromStringAsync(Crew(thinking), Ct));

        // Assert
        Assert.NotNull(error);
        Assert.Contains(expected, error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("System.Collections", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-439")]
    public async System.Threading.Tasks.Task ShouldBlameTheCrew_WhenTheCrewLevelThinkingIsMalformed()
    {
        // Arrange
        const string yaml = """
name: c
goal: g
llm:
  thinking: { effort: huge }
agents:
  a:
    role: R
    goal: G
tasks:
  t:
    description: D
    expectedOutput: O
    agent: a
""";

        // Act
        var error = await Record.ExceptionAsync(() => Loader().LoadFromStringAsync(yaml, Ct));

        // Assert
        Assert.NotNull(error);
        Assert.Contains("Crew llm.thinking.effort: 'huge'", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Agent 'a'", error.ToString(), StringComparison.Ordinal);
    }
}
