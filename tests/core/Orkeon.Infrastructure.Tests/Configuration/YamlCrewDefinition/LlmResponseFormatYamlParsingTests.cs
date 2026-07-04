using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Serialization;
using Orkeon.Infrastructure.Tests.TestDoubles;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Infrastructure.Tests.Configuration;

/// <summary>
/// LLM Response Format — YAML loader coverage.
/// Verifies the cascade <c>crew.llm.response_format</c> → <c>agent.llm.response_format</c>
/// reaches the materialised <see cref="Orkeon.Domain.SharedKernel.ValueObjects.LlmConfig"/>, and
/// that a per-task <c>llm_override:</c> block produces a
/// <see cref="Orkeon.Domain.SharedKernel.ValueObjects.LlmConfigOverride"/> on the
/// <see cref="Orkeon.Domain.Configuration.TaskConfiguration"/>.
/// </summary>
public class LlmResponseFormatYamlParsingTests
{
    private static YamlCrewDefinitionLoader BuildLoader(out TestLogger<YamlCrewDefinitionLoader> logger)
    {
        logger = new TestLogger<YamlCrewDefinitionLoader>();
        return new YamlCrewDefinitionLoader(
            new YamlDotNetSerializer(),
            new FakeFileSystemService(),
            logger);
    }

    private static YamlCrewDefinitionLoader BuildLoader()
        => new(
            new YamlDotNetSerializer(),
            new FakeFileSystemService(),
            NullLogger<YamlCrewDefinitionLoader>.Instance);

    [Fact]
    public async Task ResponseFormat_AtCrewLevel_AppliesToAllAgents()
    {
        var loader = BuildLoader();
        var yaml = """
name: c
goal: g
llm:
  model: deepseek-chat
  response_format: json_object
agents:
  a1:
    role: a1
    goal: g
  a2:
    role: a2
    goal: g
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        Assert.Equal(2, config.Agents.Count);
        foreach (var agent in config.Agents)
        {
            Assert.NotNull(agent.LlmConfig?.ResponseFormat);
            Assert.Equal("json_object", agent.LlmConfig!.ResponseFormat!.Type);
        }
    }

    [Fact]
    public async Task ResponseFormat_AtAgentLevel_OverridesCrewLevel()
    {
        var loader = BuildLoader();
        var yaml = """
name: c
goal: g
llm:
  model: deepseek-chat
  response_format: text
agents:
  json_agent:
    role: json_role
    goal: g
    llm:
      model: deepseek-chat
      response_format: json_object
  default_agent:
    role: default_role
    goal: g
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var jsonAgent = config.Agents.Single(a => a.Role == "json_role");
        Assert.NotNull(jsonAgent.LlmConfig!.ResponseFormat);
        Assert.Equal("json_object", jsonAgent.LlmConfig.ResponseFormat!.Type);
        // The "default_agent" inherits crew's "text" → maps to null (text is provider default,
        // no need to emit it to the provider).
        var defaultAgent = config.Agents.Single(a => a.Role == "default_role");
        Assert.Null(defaultAgent.LlmConfig!.ResponseFormat);
    }

    [Fact]
    public async Task ResponseFormat_OmittedEverywhere_StaysNull()
    {
        var loader = BuildLoader();
        var yaml = """
name: c
goal: g
agents:
  a:
    role: r
    goal: g
    llm:
      model: deepseek-chat
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var agent = Assert.Single(config.Agents);
        Assert.Null(agent.LlmConfig!.ResponseFormat);
    }

    [Fact]
    public async Task TaskLlmOverride_ResponseFormat_MapsToTaskConfiguration()
    {
        var loader = BuildLoader();
        var yaml = """
name: c
goal: g
agents:
  worker:
    role: worker
    goal: g
tasks:
  extract:
    description: Extract as json
    expected_output: json
    agent: worker
    llm_override:
      response_format: json_object
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var task = Assert.Single(config.Tasks);
        Assert.NotNull(task.LlmOverride);
        Assert.Equal("json_object", task.LlmOverride!.ResponseFormat!.Type);
    }

    [Fact]
    public async Task TaskLlmOverride_FullBlock_MapsAllFields()
    {
        var loader = BuildLoader();
        var yaml = """
name: c
goal: g
agents:
  worker:
    role: worker
    goal: g
tasks:
  extract:
    description: extract json
    expected_output: json
    agent: worker
    llm_override:
      response_format: json_object
      temperature: 0.0
      max_tokens: 256
      top_p: 0.5
      thinking:
        enabled: true
        effort: max
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var task = Assert.Single(config.Tasks);
        var ov = task.LlmOverride!;
        Assert.Equal("json_object", ov.ResponseFormat!.Type);
        Assert.Equal(0.0, ov.Temperature);
        Assert.Equal(256, ov.MaxTokens);
        Assert.Equal(0.5, ov.TopP);
        Assert.True(ov.Thinking!.Enabled);
        Assert.Equal("max", ov.Thinking.Effort);
    }

    [Fact]
    public async Task TaskLlmOverride_EmptyBlock_MapsToNull()
    {
        var loader = BuildLoader();
        var yaml = """
name: c
goal: g
agents:
  w:
    role: w
    goal: g
tasks:
  t:
    description: d
    expected_output: o
    agent: w
    llm_override: {}
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var task = Assert.Single(config.Tasks);
        Assert.Null(task.LlmOverride);
    }

    [Fact]
    public async Task ResponseFormat_UnknownValue_DowngradesToNullWithWarning()
    {
        var loader = BuildLoader(out var logger);
        var yaml = """
name: c
goal: g
agents:
  a:
    role: r
    goal: g
    llm:
      model: deepseek-chat
      response_format: xml
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var agent = Assert.Single(config.Agents);
        Assert.Null(agent.LlmConfig!.ResponseFormat);
        Assert.True(logger.HasLoggedWarning("response_format"),
            "Expected a structured warning when response_format has an unknown value.");
    }

    [Fact]
    public async Task ResponseFormat_CaseInsensitive_NormalizedToLowercase()
    {
        var loader = BuildLoader();
        var yaml = """
name: c
goal: g
agents:
  a:
    role: r
    goal: g
    llm:
      model: deepseek-chat
      response_format: JSON_OBJECT
""";

        var config = await loader.LoadFromStringAsync(yaml, TestContext.Current.CancellationToken);

        var agent = Assert.Single(config.Agents);
        Assert.Equal("json_object", agent.LlmConfig!.ResponseFormat!.Type);
    }
}
