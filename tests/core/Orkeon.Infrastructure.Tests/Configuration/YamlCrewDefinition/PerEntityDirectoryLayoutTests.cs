using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Configuration;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// Tests for the per-entity multi-file directory layout
/// (<c>config.yaml</c>/<c>crew.yaml</c> + <c>agents/*.yaml</c> + <c>tasks/*.yaml</c>).
/// Uses the real <see cref="YamlDotNetSerializer"/> because each entity file deserializes to the same
/// type — a type-keyed mock could not distinguish one agent file from another.
/// </summary>
public class PerEntityDirectoryLayoutTests
{
    private readonly YamlDotNetSerializer _serializer = new();

    private YamlCrewDefinitionLoader NewLoader(FakeFileSystemService fs)
        => new(_serializer, fs, NullLogger<YamlCrewDefinitionLoader>.Instance);

    [Fact]
    public async Task ShouldLoadPerEntityTree_WithIdsFromFileStems_AndSettingsFromConfigYaml()
    {
        // Arrange
        var fs = new FakeFileSystemService();
        fs.AddFile("/crews/research/config.yaml", "name: research-crew\ngoal: Research and report\nprocess: sequential");
        fs.AddFile("/crews/research/agents/researcher.yaml", "role: Researcher\ngoal: Find data");
        fs.AddFile("/crews/research/agents/writer.yaml", "role: Writer\ngoal: Write report");
        fs.AddFile("/crews/research/tasks/collect.yaml", "description: Collect data\nexpected_output: Raw data\nagent: researcher");
        var loader = NewLoader(fs);

        // Act
        var config = await loader.LoadFromDirectoryAsync("/crews/research", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("research-crew", config.Name);
        Assert.Equal("Research and report", config.Goal);
        Assert.Equal(ProcessType.Sequential, config.Process);
        Assert.Equal(2, config.Agents.Count);
        Assert.Contains(config.Agents, a => a.Role == "Researcher");
        Assert.Contains(config.Agents, a => a.Role == "Writer");
        var task = Assert.Single(config.Tasks);
        Assert.Equal("Collect data", task.Description);
        // The task's "agent: researcher" key must resolve to the agent whose file stem is "researcher".
        var researcher = config.Agents.Single(a => a.Role == "Researcher");
        Assert.Equal(researcher.Id, task.AssignedAgentId);
    }

    [Fact]
    public async Task ShouldAcceptCrewYaml_AsSettingsFileName()
    {
        // Arrange — no config.yaml, crew.yaml used as the settings file instead.
        var fs = new FakeFileSystemService();
        fs.AddFile("/crews/c/crew.yaml", "name: crew-yaml-name\ngoal: G\nprocess: sequential");
        fs.AddFile("/crews/c/agents/a.yaml", "role: R\ngoal: G");
        fs.AddFile("/crews/c/tasks/t.yaml", "description: D\nexpected_output: O\nagent: a");
        var loader = NewLoader(fs);

        // Act
        var config = await loader.LoadFromDirectoryAsync("/crews/c", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("crew-yaml-name", config.Name);
        Assert.Single(config.Agents);
    }

    [Fact]
    public async Task ShouldPreferConfigYaml_WhenBothConfigAndCrewYamlExist()
    {
        // Arrange — both settings files present; config.yaml must win.
        var fs = new FakeFileSystemService();
        fs.AddFile("/crews/c/config.yaml", "name: from-config\ngoal: G\nprocess: sequential");
        fs.AddFile("/crews/c/crew.yaml", "name: from-crew\ngoal: G\nprocess: sequential");
        fs.AddFile("/crews/c/agents/a.yaml", "role: R\ngoal: G");
        fs.AddFile("/crews/c/tasks/t.yaml", "description: D\nexpected_output: O\nagent: a");
        var loader = NewLoader(fs);

        // Act
        var config = await loader.LoadFromDirectoryAsync("/crews/c", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("from-config", config.Name);
    }

    [Fact]
    public async Task ShouldThrowInvalidOperation_WhenFlatFileAndPerEntityDirCoexist()
    {
        // Arrange — mixed tree: both agents.yaml and agents/ present.
        var fs = new FakeFileSystemService();
        fs.AddFile("/crews/mixed/config.yaml", "name: m\ngoal: G\nprocess: sequential");
        fs.AddFile("/crews/mixed/agents.yaml", "a:\n  role: R");
        fs.AddFile("/crews/mixed/agents/a.yaml", "role: R\ngoal: G");
        fs.AddFile("/crews/mixed/tasks/t.yaml", "description: D\nexpected_output: O\nagent: a");
        var loader = NewLoader(fs);

        // Act + Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => loader.LoadFromDirectoryAsync("/crews/mixed", TestContext.Current.CancellationToken));
        Assert.Contains("/crews/mixed/agents.yaml", ex.Message, StringComparison.Ordinal);
        Assert.Contains("/crews/mixed/agents", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShouldSurfaceValidatorError_WhenAgentsFolderIsEmpty()
    {
        // Arrange — per-entity mode (tasks/ exists) but agents/ is empty: no new error path,
        // the existing "at least one agent" validator rule must surface unchanged.
        var fs = new FakeFileSystemService();
        fs.AddFile("/crews/e/config.yaml", "name: e\ngoal: G\nprocess: sequential");
        fs.AddDirectory("/crews/e/agents");
        fs.AddFile("/crews/e/tasks/t.yaml", "description: D\nexpected_output: O");
        var loader = NewLoader(fs);

        // Act
        var config = await loader.LoadFromDirectoryAsync("/crews/e", TestContext.Current.CancellationToken);
        var result = loader.Validate(config);

        // Assert
        Assert.Empty(config.Agents);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("agent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ShouldLoadEquivalentConfig_RoundTripAgainstSingleFileDefinition()
    {
        // Arrange — the same crew expressed two ways: a single-file definition and a per-entity tree.
        const string singleFile = """
            name: research-crew
            goal: Research and report
            process: sequential
            agents:
              researcher:
                role: Researcher
                goal: Find data
              writer:
                role: Writer
                goal: Write report
            tasks:
              collect:
                description: Collect data
                expected_output: Raw data
                agent: researcher
              report:
                description: Write the report
                expected_output: Final report
                agent: writer
            """;

        var fs = new FakeFileSystemService();
        fs.AddFile("/crews/research/config.yaml", "name: research-crew\ngoal: Research and report\nprocess: sequential");
        fs.AddFile("/crews/research/agents/researcher.yaml", "role: Researcher\ngoal: Find data");
        fs.AddFile("/crews/research/agents/writer.yaml", "role: Writer\ngoal: Write report");
        fs.AddFile("/crews/research/tasks/collect.yaml", "description: Collect data\nexpected_output: Raw data\nagent: researcher");
        fs.AddFile("/crews/research/tasks/report.yaml", "description: Write the report\nexpected_output: Final report\nagent: writer");
        var loader = NewLoader(fs);

        // Act
        var single = await loader.LoadFromStringAsync(singleFile, TestContext.Current.CancellationToken);
        var multi = await loader.LoadFromDirectoryAsync("/crews/research", TestContext.Current.CancellationToken);

        // Assert — IDs are regenerated per load, so compare by role/description (as the round-trip suite does).
        Assert.Equal(single.Name, multi.Name);
        Assert.Equal(single.Goal, multi.Goal);
        Assert.Equal(single.Process, multi.Process);

        Assert.Equal(
            single.Agents.Select(a => a.Role).OrderBy(r => r, StringComparer.Ordinal),
            multi.Agents.Select(a => a.Role).OrderBy(r => r, StringComparer.Ordinal));

        Assert.Equal(
            single.Tasks.Select(t => t.Description).OrderBy(d => d, StringComparer.Ordinal),
            multi.Tasks.Select(t => t.Description).OrderBy(d => d, StringComparer.Ordinal));

        // Each task must be assigned to the same-role agent in both configurations.
        Assert.Equal(AgentRoleByTask(single), AgentRoleByTask(multi));
    }

    private static Dictionary<string, string> AgentRoleByTask(CrewConfiguration config)
        => config.Tasks.ToDictionary(
            t => t.Description,
            t => config.Agents.Single(a => a.Id == t.AssignedAgentId).Role,
            StringComparer.Ordinal);
}
