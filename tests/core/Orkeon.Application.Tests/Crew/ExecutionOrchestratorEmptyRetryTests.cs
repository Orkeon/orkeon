using Microsoft.Extensions.AI;
using Orkeon.Application.Crew;
using Orkeon.Domain.Tools.Protocol;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// Validates the ChatOptions shape used for the empty-final-message retry tour
/// (<see cref="ExecutionOrchestrator.CloneForToolFreeRetry"/>). The retry MUST force
/// tool_choice=none and strip the tool schema payload so the model is obliged to
/// produce text rather than more tool calls.
/// </summary>
public class ExecutionOrchestratorEmptyRetryTests
{
    private static ChatOptions BuildSourceOptions()
    {
        var options = new ChatOptions
        {
            Temperature = 0.2f,
            MaxOutputTokens = 4096,
            TopP = 0.95f,
            Tools = [],
            ToolMode = ChatToolMode.Auto,
            AdditionalProperties = new AdditionalPropertiesDictionary
            {
                ["orkeon:tool_schemas"] = new List<ToolSchema>().AsReadOnly(),
                ["orkeon:tool_mode"] = ToolCallMode.Auto,
                ["orkeon:custom"] = "preserved",
            }
        };
        return options;
    }

    [Fact]
    public void CloneForToolFreeRetry_DisablesToolsAndSetsNoneMode()
    {
        var source = BuildSourceOptions();

        var retry = ExecutionOrchestrator.CloneForToolFreeRetry(source);

        Assert.NotNull(retry.Tools);
        Assert.Empty(retry.Tools!);
        Assert.Same(ChatToolMode.None, retry.ToolMode);
    }

    [Fact]
    public void CloneForToolFreeRetry_PreservesSamplingParameters()
    {
        var source = BuildSourceOptions();

        var retry = ExecutionOrchestrator.CloneForToolFreeRetry(source);

        Assert.Equal(0.2f, retry.Temperature);
        Assert.Equal(4096, retry.MaxOutputTokens);
        Assert.Equal(0.95f, retry.TopP);
    }

    [Fact]
    public void CloneForToolFreeRetry_StripsToolSchemasButKeepsOtherAdditionalProperties()
    {
        var source = BuildSourceOptions();

        var retry = ExecutionOrchestrator.CloneForToolFreeRetry(source);

        Assert.NotNull(retry.AdditionalProperties);
        Assert.False(retry.AdditionalProperties!.ContainsKey("orkeon:tool_schemas"));
        Assert.Equal(ToolCallMode.None, retry.AdditionalProperties["orkeon:tool_mode"]);
        Assert.Equal("preserved", retry.AdditionalProperties["orkeon:custom"]);
    }

    [Fact]
    public void CloneForToolFreeRetry_NullAdditionalProperties_ReturnsCloneWithoutCrashing()
    {
        var source = new ChatOptions
        {
            Temperature = 0.1f,
            Tools = [],
            ToolMode = ChatToolMode.Auto,
            AdditionalProperties = null,
        };

        var retry = ExecutionOrchestrator.CloneForToolFreeRetry(source);

        Assert.Null(retry.AdditionalProperties);
        Assert.Same(ChatToolMode.None, retry.ToolMode);
    }
}
