using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Tools.Protocol;
using Orkeon.Interop.AgentFramework.Tests.Doubles;

namespace Orkeon.Interop.AgentFramework.Tests;

public sealed class AIAgentBridgeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LlmMessage System(string text) => new() { Role = "system", Content = text };

    private static LlmMessage User(string text) => new() { Role = "user", Content = text };

    private static LlmMessage Assistant(string text) => new() { Role = "assistant", Content = text };

    [Fact]
    public async Task The_provider_runs_the_MAF_agent_on_one_session_across_calls()
    {
        var maf = new ScriptedAIAgent { Usage = new UsageDetails { InputTokenCount = 7, OutputTokenCount = 2, TotalTokenCount = 9 } };
        var provider = new AIAgentLlmProvider(maf);

        var first = await provider.GenerateAsync("hello", cancellationToken: Ct);
        var second = await provider.ChatAsync([System("be brief"), User("again")], cancellationToken: Ct);

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
        Assert.Equal(ChatRole.System, maf.Runs[1].Messages[0].Role);
    }

    [Fact]
    public void The_provider_declares_that_it_runs_its_own_tools()
    {
        Assert.True(new AIAgentLlmProvider(new ScriptedAIAgent()).Capabilities.RunsOwnTools);
    }

    [Fact]
    public async Task The_tool_delegates_a_request_and_answers_with_the_agent_name()
    {
        var maf = new ScriptedAIAgent();
        using var tool = new AIAgentTool(maf);

        Assert.Equal("agent_scripted", tool.Name);
        Assert.Equal("Echoes what it is told", tool.Description);

        var response = await tool.CallAsync(new ToolCallRequest(tool.Name, new Dictionary<string, object?> { ["request"] = "review this" }), Ct);

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

        var response = await tool.CallAsync(new ToolCallRequest("ask_reviewer", new Dictionary<string, object?> { ["request"] = "" }), Ct);

        Assert.False(response.Success);
        Assert.Empty(maf.Runs);
        Assert.Equal("ask_reviewer", tool.Name);
    }

    [Fact]
    public void The_tool_extension_adds_a_tool_and_one_agent_cannot_hold_a_MAF_agent_as_tool_and_as_model()
    {
        // The model side is proven where it runs (AIAgentAnswersTests), not by the field it sets: a
        // field no runtime path read is how this bridge stayed inert (GAP-34).
        var maf = new ScriptedAIAgent();

        var asTool = new AgentBuilder().Role("Auditor").Goal("Audit").WithAgentFrameworkTool(maf, "ask_scripted").Build();
        var both = new AgentBuilder().Role("Auditor").Goal("Audit")
            .WithAgentFrameworkTool(maf, "ask_scripted").WithAgentFrameworkAgent(maf);

        Assert.Contains(asTool.Tools, t => t.Name == "ask_scripted");
        var error = Assert.Throws<BuilderValidationException>(both.Build);
        Assert.Contains("(ask_scripted)", error.Message, StringComparison.Ordinal);
        Assert.Contains("agent-framework:Scripted", error.Message, StringComparison.Ordinal);
    }

    // ── The session: each message once, one call at a time (GAP-34, decision 5) ────────────

    [Fact]
    public async Task A_call_that_extends_the_previous_one_sends_the_MAF_session_only_what_is_new()
    {
        // The agent loop sends the whole conversation every turn; the MAF session already holds it.
        var maf = new ScriptedAIAgent();
        var provider = new AIAgentLlmProvider(maf);
        LlmMessage[] first = [System("You are Reviewer."), User("Review the change")];

        var answer = await provider.ChatAsync(first, cancellationToken: Ct);
        await provider.ChatAsync([.. first, Assistant(answer.Content), User("Say it in one sentence")], cancellationToken: Ct);

        Assert.Equal(2, maf.Runs.Count);
        Assert.Equal(2, maf.Runs[0].Messages.Count);
        var sent = Assert.Single(maf.Runs[1].Messages);
        Assert.Equal("Say it in one sentence", sent.Text);
        Assert.Equal(ChatRole.User, sent.Role);
        Assert.Same(maf.Runs[0].Session, maf.Runs[1].Session);
        Assert.Equal(1, maf.SessionsCreated);
    }

    [Fact]
    public async Task A_call_that_does_not_extend_the_previous_one_sends_all_its_messages()
    {
        // A new task: the session keeps the previous one, as ADR-010 wants, and hears this one whole.
        var maf = new ScriptedAIAgent();
        var provider = new AIAgentLlmProvider(maf);

        await provider.ChatAsync([System("You are Reviewer."), User("Review the change")], cancellationToken: Ct);
        await provider.ChatAsync([System("You are Reviewer."), User("Review the next change")], cancellationToken: Ct);

        Assert.Equal(2, maf.Runs[1].Messages.Count);
        Assert.Equal("Review the next change", maf.Runs[1].Messages[1].Text);
        Assert.Same(maf.Runs[0].Session, maf.Runs[1].Session);
    }

    [Fact]
    public async Task A_retry_after_an_empty_answer_sends_only_the_nudge()
    {
        // The loop's empty-answer retry appends its nudge to the conversation as it was: the empty
        // answer added no message to the session.
        var answers = new Queue<string>(["", "the answer"]);
        var maf = new ScriptedAIAgent { Respond = _ => answers.Dequeue() };
        var provider = new AIAgentLlmProvider(maf);
        LlmMessage[] first = [System("You are Reviewer."), User("Review the change")];

        await provider.ChatAsync(first, cancellationToken: Ct);
        await provider.ChatAsync([.. first, User("Answer now.")], cancellationToken: Ct);

        Assert.Equal("Answer now.", Assert.Single(maf.Runs[1].Messages).Text);
    }

    [Fact]
    public async Task Two_concurrent_calls_never_run_the_MAF_agent_twice_at_once()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var maf = new ScriptedAIAgent { Hold = release.Task };
        var provider = new AIAgentLlmProvider(maf);

        var first = provider.ChatAsync([User("first task")], cancellationToken: Ct);
        await maf.RunWaiting.Task.WaitAsync(Ct);
        var second = provider.ChatAsync([User("second task")], cancellationToken: Ct);
        await Task.Yield();
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, maf.MaxRunsInFlight);
        Assert.Equal(2, maf.Runs.Count);
        Assert.Same(maf.Runs[0].Session, maf.Runs[1].Session);
    }

    // ── What the bridge does not send, it says (GAP-34, decision 7) ───────────────────────

    [Fact]
    public async Task An_option_the_bridge_does_not_send_is_a_structured_warning_once_per_run_and_option()
    {
        var logger = new StructuredLogger<AIAgentLlmProvider>();
        var provider = new AIAgentLlmProvider(new ScriptedAIAgent(), logger);
        var config = LlmConfig.OnProfile() with { Temperature = 0.2, ResponseFormat = LlmResponseFormat.JsonObject() };

        using (LlmUsageScope.Begin(LlmUsageOperations.Agent, crewId: "crew-1", agentId: "Reviewer"))
        {
            await provider.ChatAsync([User("first turn")], config, Ct);
            await provider.ChatAsync([User("second turn")], config, Ct);
        }

        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        Assert.Equal(["response_format", "temperature"], warnings.Select(w => (string?)w.Fields["Option"]).Order(StringComparer.Ordinal));
        Assert.All(warnings, warning =>
        {
            Assert.Equal("agent-framework:Scripted", warning.Fields["ProviderName"]);
            Assert.False(string.IsNullOrWhiteSpace((string?)warning.Fields["Remedy"]));
            Assert.Equal(warnings[0].EventId, warning.EventId);
        });
        Assert.NotEqual(0, warnings[0].EventId.Id);

        // Another run hears it again.
        using (LlmUsageScope.Begin(LlmUsageOperations.Agent, crewId: "crew-2", agentId: "Reviewer"))
            await provider.ChatAsync([User("next run")], config, Ct);

        Assert.Equal(4, logger.Entries.Count(entry => entry.Level == LogLevel.Warning));
    }

    [Fact]
    public async Task A_call_that_declares_nothing_warns_nothing()
    {
        var logger = new StructuredLogger<AIAgentLlmProvider>();
        var provider = new AIAgentLlmProvider(new ScriptedAIAgent(), logger);

        await provider.ChatAsync([User("plain")], cancellationToken: Ct);
        await provider.ChatAsync([User("on a configuration naming nothing")], LlmConfig.OnProfile(), Ct);

        Assert.DoesNotContain(logger.Entries, entry => entry.Level >= LogLevel.Warning);
    }
}
