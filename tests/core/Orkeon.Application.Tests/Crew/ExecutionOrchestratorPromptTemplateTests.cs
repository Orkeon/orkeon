using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Constants.Orchestration;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// CH-04: an agent's non-empty prompt template wraps the first user prompt of a task, the mark
/// replaced by the prompt composed as before; without a template the message is unchanged; the
/// turns that follow (tool results, retries) are never wrapped. The legacy provider receives the
/// whole conversation as one string, so each call is read as the system prompt, the first user
/// message, then the turns appended after it.
/// </summary>
public sealed class ExecutionOrchestratorPromptTemplateTests
{
    private const string Mark = "{{ .Prompt }}";

    private static DomainTask Task() => DomainTask.Create(
        TaskDescription.From("Explain the refund policy for {product}."),
        ExpectedOutput.From("A short answer."));

    private static SimpleExecutionContext Context() => new(
        CrewId.Create(),
        new Dictionary<string, string> { ["product"] = "Pro plan" },
        NullMemoryScope.Instance,
        [],
        CancellationToken.None);

    private static DomainAgent Agent(string? template)
    {
        var builder = new AgentBuilder().Role("Support agent").Goal("Answer customer questions");
        if (template is not null)
            builder.PromptTemplate(template);
        return builder.Build();
    }

    private static async System.Threading.Tasks.Task<(ScriptedBasicLlmProvider Llm, DomainTask Task, SimpleExecutionContext Context)> RunAsync(
        string? template, params string[] answers)
    {
        var llm = new ScriptedBasicLlmProvider();
        foreach (var answer in answers)
            llm.Enqueue(answer);
        var orchestrator = new ExecutionOrchestrator(NullLogger<ExecutionOrchestrator>.Instance, llm);
        var task = Task();
        var context = Context();

        var result = await orchestrator.ExecuteTaskCoreAsync(Agent(template), task, context, TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        return (llm, task, context);
    }

    private static string Unix(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    [Trait("BR", "CH-04")]
    public async System.Threading.Tasks.Task ShouldSendTodaysUserPrompt_WhenAgentHasNoPromptTemplate()
    {
        var task = Task();
        var plan = ExecutionPlan.Create([new PlannedTask(task.Id, "1. Read the policy.")]);
        var llm = new ScriptedBasicLlmProvider();
        var orchestrator = new ExecutionOrchestrator(NullLogger<ExecutionOrchestrator>.Instance, llm);

        using (CrewPlanScope.Begin(plan))
        {
            var result = await orchestrator.ExecuteTaskCoreAsync(Agent(null), task, Context(), TestContext.Current.CancellationToken);
            Assert.True(result.Success, result.Error);
        }

        var sent = Unix(Assert.Single(llm.ReceivedPrompts));
        var todays =
            "Task:\nExplain the refund policy for Pro plan.\n\n"
            + "Expected output: A short answer.\n\n"
            + Unix(PromptDefaults.PlanSectionHeader) + "\n1. Read the policy.\n"
            + "\nContext variables:\n- product: Pro plan\n";
        Assert.EndsWith(todays + "\n", sent, StringComparison.Ordinal);
        Assert.DoesNotContain("Avant", sent, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public async System.Threading.Tasks.Task ShouldWrapTaskPrompt_WhenAgentTemplateHasOneMark()
    {
        var (llm, task, context) = await RunAsync("Avant\n" + Mark + "\nAprès");

        var composed = AgentPromptComposer.BuildUserPrompt(task, context);
        var sent = Unix(Assert.Single(llm.ReceivedPrompts));
        Assert.Contains("Avant\n" + Unix(composed) + "\nAprès", sent, StringComparison.Ordinal);
        Assert.Contains("Explain the refund policy for Pro plan.", sent, StringComparison.Ordinal);
        Assert.DoesNotContain(Mark, sent, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public async System.Threading.Tasks.Task ShouldWrapTwice_WhenAgentTemplateHasTwoMarks()
    {
        var (llm, task, context) = await RunAsync("Avant\n" + Mark + "\nMilieu\n" + Mark + "\nAprès");

        var composed = Unix(AgentPromptComposer.BuildUserPrompt(task, context));
        var sent = Unix(Assert.Single(llm.ReceivedPrompts));
        Assert.Contains("Avant\n" + composed + "\nMilieu\n" + composed + "\nAprès", sent, StringComparison.Ordinal);
        Assert.DoesNotContain(Mark, sent, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public async System.Threading.Tasks.Task ShouldLeaveRetryAndToolMessagesUnwrapped_WhenAgentHasTemplate()
    {
        var toolCall = "[TOOL_CALL]{tool => \"missing_tool\", args => {--path \"/src\"}}[/TOOL_CALL]";

        var (llm, _, _) = await RunAsync("Avant\n" + Mark + "\nAprès", toolCall, "Final answer.");

        Assert.Equal(2, llm.ReceivedPrompts.Count);
        var first = Unix(llm.ReceivedPrompts[0]);
        var second = Unix(llm.ReceivedPrompts[1]);
        Assert.Contains("Avant\n", first, StringComparison.Ordinal);
        Assert.Contains("\nAprès", first, StringComparison.Ordinal);
        Assert.StartsWith(first, second, StringComparison.Ordinal);
        var appended = second[first.Length..];
        Assert.False(string.IsNullOrWhiteSpace(appended));
        Assert.DoesNotContain("Avant", appended, StringComparison.Ordinal);
        Assert.DoesNotContain("Après", appended, StringComparison.Ordinal);
    }
}
