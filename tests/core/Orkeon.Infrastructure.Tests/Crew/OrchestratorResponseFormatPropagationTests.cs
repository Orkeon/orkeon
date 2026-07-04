using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;

namespace Orkeon.Infrastructure.Tests.Crew;

/// <summary>
/// LLM Response Format — orchestrator propagation contract.
/// This test asserts the integration contract that <c>ExecutionOrchestrator</c> applies
/// upstream of every provider call: fuse <c>agent.LlmConfig</c> (base) ⊕
/// <c>task.LlmOverride</c> via <see cref="LlmConfigResolver"/>, and the result is what
/// flows into <c>provider.ChatAsync</c>. The orchestrator itself is too big to instantiate
/// in isolation here — the test instead pins the contract by composing the same building
/// blocks the orchestrator uses.
/// </summary>
public class OrchestratorResponseFormatPropagationTests
{
    [Fact]
    public void TaskOverride_JsonObject_TakesPrecedenceOverAgentText()
    {
        // Arrange — agent says text, task says json_object (extraction case)
        var agentConfig = LlmConfig.Create("deepseek-chat") with
        {
            ResponseFormat = LlmResponseFormat.Text()
        };
        var task = new CrewTaskBuilder()
            .Description("Extract invoice fields as JSON")
            .ExpectedOutput("JSON")
            .WithResponseFormat("json_object")
            .Build();

        // Act — same fusion the orchestrator performs before each ChatAsync
        var effective = LlmConfigResolver.Resolve(
            baseConfig: agentConfig,
            taskOverride: task.LlmOverride,
            callOverride: null);

        // Assert — task override wins
        Assert.NotNull(effective.ResponseFormat);
        Assert.Equal("json_object", effective.ResponseFormat!.Type);
    }

    [Fact]
    public void NoTaskOverride_AgentResponseFormatStays()
    {
        var agentConfig = LlmConfig.Create("deepseek-chat") with
        {
            ResponseFormat = LlmResponseFormat.JsonObject()
        };
        var task = new CrewTaskBuilder()
            .Description("Reply free-form")
            .ExpectedOutput("text")
            .Build();

        var effective = LlmConfigResolver.Resolve(agentConfig, task.LlmOverride, null);

        Assert.Equal("json_object", effective.ResponseFormat!.Type);
    }

    [Fact]
    public void NeitherSet_EffectiveResponseFormatIsNull()
    {
        var agentConfig = LlmConfig.Create("deepseek-chat");
        var task = new CrewTaskBuilder()
            .Description("d")
            .ExpectedOutput("o")
            .Build();

        var effective = LlmConfigResolver.Resolve(agentConfig, task.LlmOverride, null);

        Assert.Null(effective.ResponseFormat);
    }
}
