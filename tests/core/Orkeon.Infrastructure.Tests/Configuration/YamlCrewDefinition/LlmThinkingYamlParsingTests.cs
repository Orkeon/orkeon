using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// Regression coverage for Experiment 07 friction #4: YAML keys for the DeepSeek thinking
/// mode (<c>thinking:</c> block with <c>enabled</c>/<c>effort</c>) and the <c>topP</c> /
/// <c>top_p</c> sampling probability must round-trip through the real
/// <see cref="YamlDotNetSerializer"/> + <see cref="YamlCrewDefinitionLoader"/> pipeline
/// into the <see cref="Orkeon.Domain.SharedKernel.ValueObjects.LlmConfig"/> attached to each agent.
/// </summary>
public class LlmThinkingYamlParsingTests
{
    private static YamlCrewDefinitionLoader BuildLoader()
        => new(
            new YamlDotNetSerializer(),
            new FakeFileSystemService(),
            NullLogger<YamlCrewDefinitionLoader>.Instance);

    [Fact]
    public async Task LoadFromString_ShouldMapThinking_FromCamelCaseYaml()
    {
        var loader = BuildLoader();
        var yaml = """
name: thinking-crew
goal: x
agents:
  planner:
    role: planner
    goal: plan
    llm:
      model: deepseek-v4-pro
      temperature: 0.2
      topP: 0.9
      thinking:
        enabled: true
        effort: max
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var agent = Assert.Single(config.Agents);
        Assert.NotNull(agent.LlmConfig);
        Assert.Equal("deepseek-v4-pro", agent.LlmConfig!.Model);
        Assert.Equal(0.9, agent.LlmConfig.TopP);
        Assert.NotNull(agent.LlmConfig.Thinking);
        Assert.Equal(true, agent.LlmConfig.Thinking!.Enabled);
        Assert.Equal("max", agent.LlmConfig.Thinking.Effort);
    }

    [Fact]
    public async Task LoadFromString_ShouldMapThinking_FromSnakeCaseYaml()
    {
        // Snake_case crew YAML authors write `top_p:` and `thinking.enabled` snake_case — fix #2
        // makes the loader forgiving and fix #4 ensures these still propagate to LlmConfig.
        var loader = BuildLoader();
        var yaml = """
name: thinking-crew
goal: x
agents:
  planner:
    role: planner
    goal: plan
    llm:
      model: deepseek-v4-flash
      top_p: 0.75
      thinking:
        enabled: false
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var agent = Assert.Single(config.Agents);
        Assert.Equal("deepseek-v4-flash", agent.LlmConfig!.Model);
        Assert.Equal(0.75, agent.LlmConfig.TopP);
        Assert.Equal(false, agent.LlmConfig.Thinking!.Enabled);
        Assert.Null(agent.LlmConfig.Thinking.Effort);
    }

    [Fact]
    public async Task LoadFromString_ShouldLeaveThinkingNull_WhenAbsent()
    {
        var loader = BuildLoader();
        var yaml = """
name: c
goal: g
agents:
  a:
    role: a
    goal: g
    llm:
      model: deepseek-v4-flash
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var agent = Assert.Single(config.Agents);
        Assert.NotNull(agent.LlmConfig);
        Assert.Null(agent.LlmConfig!.Thinking);
        Assert.Equal(1.0, agent.LlmConfig.TopP);
    }

    [Fact]
    public async Task LoadFromString_ShouldNotMaterializeThinking_WhenBlockHasNoMeaningfulFields()
    {
        // An empty thinking block should be treated as "leave provider default in place"
        // (Thinking stays null) rather than producing a no-op LlmThinkingConfig instance.
        var loader = BuildLoader();
        var yaml = """
name: c
goal: g
agents:
  a:
    role: a
    goal: g
    llm:
      model: deepseek-v4-flash
      thinking: {}
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var agent = Assert.Single(config.Agents);
        Assert.Null(agent.LlmConfig!.Thinking);
    }
}
