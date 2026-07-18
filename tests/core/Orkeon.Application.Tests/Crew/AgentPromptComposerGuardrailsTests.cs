using Orkeon.Application.Crew.Execution;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task;
using Orkeon.Domain.Tools;
using Orkeon.Domain.Tools.Protocol;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// Verifies that task-level guardrails (P2-O-04) reach the system prompt via
/// <see cref="AgentPromptComposer.BuildSystemPrompt"/>, alongside — and after — the agent's own
/// guardrails, without changing agent-level rendering.
/// </summary>
public class AgentPromptComposerGuardrailsTests
{
    [Fact]
    public void BuildSystemPrompt_ShouldInjectTaskGuardrails_IntoSystemPrompt()
    {
        var agent = new AgentBuilder().Role("Analyst").Goal("Analyze").Build();
        var task = new CrewTaskBuilder()
            .Description("Do work")
            .ExpectedOutput("Result")
            .WithGuardrails(new GuardrailsConfig
            {
                Header = "TASK RULES:",
                Rules = ["cite every source"]
            })
            .Build();

        var prompt = AgentPromptComposer.BuildSystemPrompt(agent, task, supportsNativeToolCalling: true);

        Assert.Contains("TASK RULES:", prompt, StringComparison.Ordinal);
        Assert.Contains("1. cite every source", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSystemPrompt_ShouldLeaveAgentGuardrailsUnchanged_WhenTaskHasNone()
    {
        var agent = new AgentBuilder()
            .Role("Analyst").Goal("Analyze")
            .WithGuardrails(new GuardrailsConfig { Header = "AGENT RULES:", Rules = ["stay factual"] })
            .Build();
        var task = new CrewTaskBuilder().Description("Do work").ExpectedOutput("Result").Build();

        var prompt = AgentPromptComposer.BuildSystemPrompt(agent, task, supportsNativeToolCalling: true);

        // Agent guardrails still render; no phantom task section appears.
        Assert.Contains("AGENT RULES:", prompt, StringComparison.Ordinal);
        Assert.Contains("1. stay factual", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("TASK RULES:", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildSystemPrompt_ShouldApplyBothLevels_AgentRulesBeforeTaskRules()
    {
        var agent = new AgentBuilder()
            .Role("Analyst").Goal("Analyze")
            .WithGuardrails(new GuardrailsConfig { Header = "AGENT RULES:", Rules = ["agent-rule-alpha"] })
            .Build();
        var task = new CrewTaskBuilder()
            .Description("Do work").ExpectedOutput("Result")
            .WithGuardrails(new GuardrailsConfig { Header = "TASK RULES:", Rules = ["task-rule-beta"] })
            .Build();

        var prompt = AgentPromptComposer.BuildSystemPrompt(agent, task, supportsNativeToolCalling: true);

        // Both apply; the agent section precedes the task section.
        Assert.Contains("agent-rule-alpha", prompt, StringComparison.Ordinal);
        Assert.Contains("task-rule-beta", prompt, StringComparison.Ordinal);
        Assert.True(
            prompt.IndexOf("AGENT RULES:", StringComparison.Ordinal)
                < prompt.IndexOf("TASK RULES:", StringComparison.Ordinal),
            "Agent guardrails must render before task guardrails.");
    }

    [Fact]
    public void BuildSystemPrompt_ShouldGateTaskToolRules_ByExecutingAgentTools()
    {
        var agent = new AgentBuilder()
            .Role("Analyst").Goal("Analyze")
            .WithTool(new StubTool("file_write"))
            .Build();
        var task = new CrewTaskBuilder()
            .Description("Do work").ExpectedOutput("Result")
            .WithGuardrails(new GuardrailsConfig
            {
                Header = "TASK RULES:",
                ToolRules = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["file_write"] = ["never overwrite"],   // agent HAS this tool → rendered
                    ["web_search"] = ["cite domains"]        // agent LACKS this tool → skipped
                }
            })
            .Build();

        var prompt = AgentPromptComposer.BuildSystemPrompt(agent, task, supportsNativeToolCalling: true);

        Assert.Contains("[file_write] never overwrite", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("web_search", prompt, StringComparison.Ordinal);
    }

    /// <summary>Minimal ITool stub — only Name matters for tool-rule gating.</summary>
    private sealed class StubTool : ITool
    {
        public string Name { get; }
        public string Description => "stub";
        public ToolSchema Schema { get; }

        public StubTool(string name)
        {
            Name = name;
            Schema = new ToolSchema(name, "stub", new Dictionary<string, ParameterSchema>());
        }

        public System.Threading.Tasks.Task<ToolCallResponse> CallAsync(
            Orkeon.Domain.Tools.Protocol.ToolCallRequest request, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new ToolCallResponse(true, "stub result", null));

        public System.Threading.Tasks.Task<ToolResult> ExecuteAsync(
            string input, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(ToolResult.CreateSuccess("stub result"));

        public bool ValidateInput(string input) => true;
    }
}
