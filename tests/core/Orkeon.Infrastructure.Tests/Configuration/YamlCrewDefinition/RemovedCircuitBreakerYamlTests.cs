using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// GAP-07: the task state machine and the <c>circuitBreaker:</c> blocks that configured it are
/// gone. The task block never ran, and the crew block was read by Graph alone, where it doubled
/// <c>graphConfig</c>. The deserializer ignores unknown keys, so a crew that still writes one is
/// refused at load with a message that names <c>graphConfig</c> — never silently ignored.
/// </summary>
public class RemovedCircuitBreakerYamlTests
{
    private static YamlCrewDefinitionLoader BuildLoader(FakeFileSystemService? fs = null)
        => new(new YamlDotNetSerializer(), fs ?? new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

    [Fact]
    public async Task LoadFromString_CrewLevelCircuitBreaker_IsRefusedAndNamesGraphConfig()
    {
        var yaml = """
name: c
goal: g
process: graph
circuitBreaker:
  preset: strict
  maxTransitions: 30
agents:
  a: { role: R, goal: G }
tasks:
  t: { description: D, expectedOutput: O, agent: a }
""";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildLoader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken));

        Assert.Contains("'circuitBreaker'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("graphConfig", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadFromString_TaskLevelCircuitBreaker_IsRefusedAndNamesTheTask()
    {
        var yaml = """
name: c
goal: g
agents:
  a: { role: R, goal: G }
tasks:
  review:
    description: D
    expectedOutput: O
    agent: a
    circuitBreaker:
      maxToolCallsPerRound: 30
      maxRetries: 2
""";

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildLoader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken));

        Assert.Contains("'tasks.review.circuitBreaker'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("graphConfig", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadFromDirectory_PerEntityTaskFileWithCircuitBreaker_IsRefused()
    {
        var fs = new FakeFileSystemService();
        fs.AddFile("/crews/c/config.yaml", "name: c\ngoal: G\nprocess: sequential");
        fs.AddFile("/crews/c/agents/a.yaml", "role: R\ngoal: G");
        fs.AddFile("/crews/c/tasks/t.yaml", "description: D\nexpectedOutput: O\nagent: a\ncircuitBreaker:\n  preset: strict");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildLoader(fs).LoadFromDirectoryAsync("/crews/c", TestContext.Current.CancellationToken));

        Assert.Contains("'tasks.t.circuitBreaker'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadFromDirectory_FlatLayoutWithCircuitBreakers_IsRefused()
    {
        var fs = new FakeFileSystemService();
        fs.AddFile("/crews/f/crew.yaml", "name: f\ngoal: G\ncircuitBreaker:\n  preset: strict");
        fs.AddFile("/crews/f/agents.yaml", "a:\n  role: R\n  goal: G");
        fs.AddFile("/crews/f/tasks.yaml", "t:\n  description: D\n  agent: a\n  circuitBreaker:\n    maxRetries: 2");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildLoader(fs).LoadFromDirectoryAsync("/crews/f", TestContext.Current.CancellationToken));

        Assert.Contains("'circuitBreaker'", ex.Message, StringComparison.Ordinal);
        Assert.Contains("'tasks.t.circuitBreaker'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadFromString_GraphConfig_StillLoads()
    {
        var yaml = """
name: c
goal: g
process: graph
graphConfig:
  circuitBreakerPreset: permissive
  maxTransitions: 30
agents:
  a: { role: R, goal: G }
tasks:
  t: { description: D, expectedOutput: O, agent: a }
""";

        var config = await BuildLoader().LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.Equal(30, config.GraphConfig!.MaxTransitions);
    }
}
