using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// Regression coverage for Experiment 07 friction #7: a per-agent <c>llm:</c> block must
/// override the crew-level default on a field-by-field basis. Previously
/// <see cref="YamlCrewDefinitionLoader"/> replaced the whole block when the agent declared
/// one, silently dropping the crew default's MaxTokens / Temperature.
/// </summary>
public class PerAgentLlmOverrideTests
{
    private static YamlCrewDefinitionLoader BuildLoader()
        => new(new YamlDotNetSerializer(), new FakeFileSystemService(), NullLogger<YamlCrewDefinitionLoader>.Instance);

    [Fact]
    public async Task AgentOverride_ShouldKeepCrewDefaults_ForFieldsTheAgentDoesNotSet()
    {
        var loader = BuildLoader();
        var yaml = """
name: c
goal: g
llm:
  model: deepseek-v4-flash
  temperature: 0.4
  maxTokens: 2048
  topP: 0.95
agents:
  planner:
    role: planner
    goal: plan
    llm:
      model: deepseek-v4-pro
  classifier:
    role: classifier
    goal: classify
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);
        var planner = config.Agents.Single(a => a.Role == "planner");
        var classifier = config.Agents.Single(a => a.Role == "classifier");

        // Planner picks up the model override but inherits crew defaults for every
        // field it did NOT explicitly set (this is the actual friction #7 fix).
        Assert.Equal("deepseek-v4-pro", planner.LlmConfig!.Model);
        Assert.Equal(0.4, planner.LlmConfig.Temperature);
        Assert.Equal(2048, planner.LlmConfig.MaxTokens);
        Assert.Equal(0.95, planner.LlmConfig.TopP);

        // Classifier has no agent-level llm block at all → fully uses crew defaults.
        Assert.Equal("deepseek-v4-flash", classifier.LlmConfig!.Model);
        Assert.Equal(0.4, classifier.LlmConfig.Temperature);
        Assert.Equal(2048, classifier.LlmConfig.MaxTokens);
    }

    [Fact]
    public async Task AgentThinkingOverride_ShouldMergeFieldByField_OnTopOfCrewThinking()
    {
        var loader = BuildLoader();
        var yaml = """
name: c
goal: g
llm:
  model: deepseek-v4-flash
  thinking:
    enabled: true
    effort: high
agents:
  planner:
    role: planner
    goal: plan
    llm:
      thinking:
        effort: max
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);
        var planner = config.Agents.Single();

        // Effort wins from the agent, Enabled inherits from the crew default.
        Assert.NotNull(planner.LlmConfig!.Thinking);
        Assert.Equal(true, planner.LlmConfig.Thinking!.Enabled);
        Assert.Equal("max", planner.LlmConfig.Thinking.Effort);
    }

    [Fact]
    public async Task AgentLlmBlockAbsent_ShouldFallBackEntirelyToCrewDefault()
    {
        var loader = BuildLoader();
        var yaml = """
name: c
goal: g
llm:
  model: m1
  temperature: 0.3
agents:
  a:
    role: a
    goal: g
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);
        var agent = config.Agents.Single();
        Assert.Equal("m1", agent.LlmConfig!.Model);
        Assert.Equal(0.3, agent.LlmConfig.Temperature);
    }
}
