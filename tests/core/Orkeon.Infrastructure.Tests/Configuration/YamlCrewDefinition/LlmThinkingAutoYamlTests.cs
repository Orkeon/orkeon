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
}
