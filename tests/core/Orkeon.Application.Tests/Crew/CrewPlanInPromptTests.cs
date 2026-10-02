using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Constants.Orchestration;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Constants.Crew;
using Orkeon.Domain.Crew;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using ExecutionPlan = Orkeon.Domain.Crew.ExecutionPlan;

namespace Orkeon.Application.Tests.Crew;

/// <summary>
/// GAP-31: the crew's plan reaches each task through the one point every mode's executions go
/// through — <see cref="AgentPromptComposer.BuildUserPrompt"/>, called by
/// <see cref="ExecutionOrchestrator.ExecuteTaskCoreAsync"/> with the plan the run's
/// <see cref="CrewPlanScope"/> holds for that task. After the task (description, expected output,
/// deliverable), before the variables; bounded; and a prompt without a plan is the prompt it was.
/// </summary>
public sealed class CrewPlanInPromptTests
{
    private static DomainTask Task(string description) => DomainTask.Create(
        TaskDescription.From(description),
        ExpectedOutput.From("A short answer."));

    private static SimpleExecutionContext Context() => new(
        CrewId.Create(),
        new Dictionary<string, string> { ["product"] = "Pro plan" },
        NullMemoryScope.Instance,
        [],
        CancellationToken.None);

    // ── the composer ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_plan_follows_the_task_and_precedes_the_variables()
    {
        var prompt = AgentPromptComposer.BuildUserPrompt(
            Task("Explain the refund policy for {product}."), Context(), knowledgeContext: null, plan: "1. Read the policy.\n2. Answer.");

        var expected = prompt.IndexOf(PromptDefaults.ExpectedOutputPrefix, StringComparison.Ordinal);
        var header = prompt.IndexOf(PromptDefaults.PlanSectionHeader, StringComparison.Ordinal);
        var plan = prompt.IndexOf("1. Read the policy.\n2. Answer.", StringComparison.Ordinal);
        var variables = prompt.IndexOf(PromptDefaults.ContextVariablesHeader, StringComparison.Ordinal);
        Assert.True(expected >= 0 && header > expected && plan > header && variables > plan, prompt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Without_a_plan_the_prompt_is_byte_identical(string? plan)
    {
        var task = Task("Explain the refund policy for {product}.");
        var context = Context();

        var withoutPlan = AgentPromptComposer.BuildUserPrompt(task, context);

        Assert.Equal(withoutPlan, AgentPromptComposer.BuildUserPrompt(task, context, knowledgeContext: null, plan: plan));
        Assert.DoesNotContain(PromptDefaults.PlanSectionHeader, withoutPlan, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plan_longer_than_its_bound_is_cut_with_a_mark()
    {
        var longPlan = new string('p', PlanningDefaults.MaxPlanChars) + "BEYOND-THE-BOUND";

        var prompt = AgentPromptComposer.BuildUserPrompt(
            Task("Explain the refund policy."), Context(), knowledgeContext: null, plan: longPlan);

        Assert.Contains(new string('p', PlanningDefaults.MaxPlanChars) + PlanningDefaults.TruncationMarker, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("BEYOND-THE-BOUND", prompt, StringComparison.Ordinal);
    }

    // ── the run's scope, read by the orchestrator ────────────────────────────────────────

    private sealed class RecordingLlmProvider : IBasicLlmProvider
    {
        public string Name => "recording";

        public List<string> ReceivedMessages { get; } = [];

        public System.Threading.Tasks.Task<string> ChatAsync(
            string message, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            ReceivedMessages.Add(message);
            return System.Threading.Tasks.Task.FromResult("Final answer.");
        }

        public System.Threading.Tasks.Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(true);
    }

    private sealed class NullPlanner : Orkeon.Domain.Crew.Planning.IAgentPlanner
    {
        public System.Threading.Tasks.Task<Orkeon.Domain.Crew.Planning.TaskPlan> CreatePlanAsync(
            DomainTask task, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(new Orkeon.Domain.Crew.Planning.TaskPlan());

        public System.Threading.Tasks.Task<Orkeon.Domain.Crew.Planning.TaskPlan> RefinePlanAsync(
            Orkeon.Domain.Crew.Planning.TaskPlan plan,
            Orkeon.Domain.Crew.Planning.PlanFeedback feedback,
            CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(plan);

        public System.Threading.Tasks.Task<Orkeon.Domain.Crew.Planning.PlanValidationResult> ValidatePlanAsync(
            Orkeon.Domain.Crew.Planning.TaskPlan plan, CancellationToken cancellationToken = default)
            => System.Threading.Tasks.Task.FromResult(
                new Orkeon.Domain.Crew.Planning.PlanValidationResult { IsValid = true });
    }

    private static async System.Threading.Tasks.Task<string> PromptOfAsync(DomainTask task)
    {
        var llm = new RecordingLlmProvider();
        var orchestrator = new ExecutionOrchestrator(NullLogger<ExecutionOrchestrator>.Instance, llm, new NullPlanner());
        var agent = new AgentBuilder().Role("Support agent").Goal("Answer customer questions").Build();

        var result = await orchestrator.ExecuteTaskCoreAsync(agent, task, Context(), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        return Assert.Single(llm.ReceivedMessages);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_task_reads_its_own_plan_from_the_runs_scope_and_no_other()
    {
        var refunds = Task("Explain the refund policy.");
        var seats = Task("Count the seats of the Pro plan.");
        var plan = ExecutionPlan.Create(
        [
            new PlannedTask(refunds.Id, "PLAN-REFUNDS"),
            new PlannedTask(seats.Id, "PLAN-SEATS"),
        ]);

        string prompt;
        using (CrewPlanScope.Begin(plan))
            prompt = await PromptOfAsync(refunds);

        Assert.Contains(PromptDefaults.PlanSectionHeader, prompt, StringComparison.Ordinal);
        Assert.Contains("PLAN-REFUNDS", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("PLAN-SEATS", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task Outside_a_run_scope_or_in_one_without_a_plan_the_prompt_has_no_section()
    {
        var task = Task("Explain the refund policy.");

        var outside = await PromptOfAsync(task);
        string inEmptyScope;
        using (CrewPlanScope.Begin(ExecutionPlan.Empty))
            inEmptyScope = await PromptOfAsync(task);

        Assert.DoesNotContain(PromptDefaults.PlanSectionHeader, outside, StringComparison.Ordinal);
        Assert.Equal(outside, inEmptyScope);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_run_nested_in_a_task_sees_its_own_plan_only()
    {
        // A crew run from inside a task opens its own scope: the outer run's plan stops at it, and
        // comes back once the nested run's scope is closed.
        var outer = Task("Explain the refund policy.");
        var outerPlan = ExecutionPlan.Create([new PlannedTask(outer.Id, "OUTER-PLAN")]);

        using (CrewPlanScope.Begin(outerPlan))
        {
            using (CrewPlanScope.Begin(ExecutionPlan.Empty))
                Assert.Null(CrewPlanScope.InstructionsFor(outer.Id));

            Assert.Equal("OUTER-PLAN", CrewPlanScope.InstructionsFor(outer.Id));
        }

        Assert.Null(CrewPlanScope.InstructionsFor(outer.Id));
        Assert.Contains("OUTER-PLAN", await PromptWithScopeAsync(outer, outerPlan), StringComparison.Ordinal);
    }

    private static async System.Threading.Tasks.Task<string> PromptWithScopeAsync(DomainTask task, ExecutionPlan plan)
    {
        using var scope = CrewPlanScope.Begin(plan);
        return await PromptOfAsync(task);
    }
}
