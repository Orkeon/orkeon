using Orkeon.Domain.Agent;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Interop.AgentFramework.Tests.Doubles;

namespace Orkeon.Interop.AgentFramework.Tests;

public sealed class AIAgentBridgeTests
{
    [Fact]
    public async Task The_provider_runs_the_MAF_agent_on_one_session_across_calls()
    {
        var maf = new ScriptedAIAgent { Usage = new Microsoft.Extensions.AI.UsageDetails { InputTokenCount = 7, OutputTokenCount = 2, TotalTokenCount = 9 } };
        var provider = new AIAgentLlmProvider(maf);

        var first = await provider.GenerateAsync("hello", cancellationToken: TestContext.Current.CancellationToken);
        var second = await provider.ChatAsync([new LlmMessage { Role = "system", Content = "be brief" }, new LlmMessage { Role = "user", Content = "again" }], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("scripted reply <- hello", first.Content);
        Assert.Equal("scripted reply <- again", second.Content);
        Assert.Equal(9, first.TokensUsed);
        Assert.Equal(7, first.PromptTokens);
        Assert.Equal(2, first.CompletionTokens);
        Assert.Equal("Scripted", first.Model);
        Assert.Equal("agent-framework:Scripted", provider.Name);
        Assert.Equal(1, maf.SessionsCreated);
        Assert.Equal(2, maf.Runs.Count);
        Assert.Same(maf.Runs[0].Session, maf.Runs[1].Session);
        Assert.Equal(Microsoft.Extensions.AI.ChatRole.System, maf.Runs[1].Messages[0].Role);
    }

    [Fact]
    public async Task The_tool_delegates_a_request_and_answers_with_the_agent_name()
    {
        var maf = new ScriptedAIAgent();
        using var tool = new AIAgentTool(maf);

        Assert.Equal("agent_scripted", tool.Name);
        Assert.Equal("Echoes what it is told", tool.Description);

        var response = await tool.CallAsync(new ToolCallRequest(tool.Name, new Dictionary<string, object?> { ["request"] = "review this" }), TestContext.Current.CancellationToken);

        Assert.True(response.Success, response.Error);
        var result = Assert.IsType<Dictionary<string, object?>>(response.Result);
        Assert.Equal("scripted reply <- review this", result["answer"]?.ToString());
        Assert.Equal("Scripted", result["agent"]?.ToString());
    }

    [Fact]
    public async Task The_tool_refuses_an_empty_request_before_calling_the_agent()
    {
        var maf = new ScriptedAIAgent();
        using var tool = new AIAgentTool(maf, "ask_reviewer", "Ask the reviewer");

        var response = await tool.CallAsync(new ToolCallRequest("ask_reviewer", new Dictionary<string, object?> { ["request"] = "" }), TestContext.Current.CancellationToken);

        Assert.False(response.Success);
        Assert.Empty(maf.Runs);
        Assert.Equal("ask_reviewer", tool.Name);
    }

    [Fact]
    public void The_builder_extensions_wire_the_agent_as_brain_and_as_tool()
    {
        var maf = new ScriptedAIAgent();

        var asBrain = new AgentBuilder().Role("Auditor").Goal("Audit").WithAgentFrameworkAgent(maf).Build();
        var asTool = new AgentBuilder().Role("Auditor").Goal("Audit").WithAgentFrameworkTool(maf, "ask_scripted").Build();

        Assert.IsType<AIAgentLlmProvider>(asBrain.FunctionCallingLlm);
        Assert.Contains(asTool.Tools, t => t.Name == "ask_scripted");
    }
}
