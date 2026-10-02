using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Services.Security;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.Planning;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// GAP-06: every tool call of an agent loop is a step for the registered
/// <see cref="Orkeon.Application.Callback.ICallbackHandler"/>s — started, then completed —
/// whether the tool succeeds, fails, is blocked by the guardian or throws.
/// </summary>
public class AgentStepCallbackTests
{
    private static DomainAgent BuildAgent(params IBaseTool[] tools)
    {
        var builder = new AgentBuilder().Role("Researcher").Goal("Research").MaxIterations(5);
        foreach (var tool in tools)
            builder = builder.WithTool(tool);
        return builder.Build();
    }

    private static DomainTask BuildTask() =>
        DomainTask.Create(TaskDescription.From("Summarise the page"), ExpectedOutput.From("A summary"));

    private static SimpleExecutionContext BuildContext() =>
        new(CrewId.From(Guid.NewGuid()), [], NullMemoryScope.Instance, []);

    private static (ICallbackOrchestrator Callbacks, RecordingCallbackHandler Handler) Callbacks()
    {
        var handler = new RecordingCallbackHandler();
        return (new CallbackOrchestrator(NullLogger<CallbackOrchestrator>.Instance, [handler]), handler);
    }

    private static NativeToolCallingAgentLoop NativeLoop(
        ScriptedFullLlmProvider provider, IBaseTool[] tools, ICallbackOrchestrator callbacks, ToolInvocationPipeline? pipeline = null)
    {
        var logger = new SpyExecutionLogger();
        return new NativeToolCallingAgentLoop(
            logger, provider, new FakeToolCallingStrategy(new OpenAiShapedToolCallParser()), tools,
            new LlmCallGate(logger, new ScriptedBasicLlmProvider(), rateLimiter: null),
            StepNotifyingToolInvocationPipeline.Wrap(pipeline ?? ToolInvocationPipeline.Unguarded, callbacks));
    }

    private static ExecutionInvocationContext Invocation(DomainAgent agent, DomainTask task) =>
        new(agent, task, "system", "user", BuildContext(), [], System.Diagnostics.Stopwatch.StartNew());

    [Fact]
    public async System.Threading.Tasks.Task NativeLoop_reports_each_tool_call_as_a_started_then_completed_step()
    {
        var search = new SpyTool("web_search", result: "3 rows");
        var scrape = new SpyTool("web_scrape", result: "page text");
        var provider = new ScriptedFullLlmProvider();
        provider.EnqueueOpenAiToolCall("call-1", "web_search", "{\"input\":\"orkeon\"}");
        provider.EnqueueOpenAiToolCall("call-2", "web_scrape", "{\"input\":\"https://example.com\"}");
        provider.EnqueueText("A summary.");
        var (callbacks, handler) = Callbacks();
        var agent = BuildAgent(search, scrape);
        var task = BuildTask();

        var result = await NativeLoop(provider, [search, scrape], callbacks)
            .ExecuteAsync(Invocation(agent, task), 5, TestContext.Current.CancellationToken);

        Assert.Equal(AgentExitReason.Completed, result.ExitReason);
        Assert.Equal(
            ["StepStarted:tool:web_search", "StepCompleted:tool:web_search:True",
             "StepStarted:tool:web_scrape", "StepCompleted:tool:web_scrape:True"],
            handler.Hooks);
        var started = handler.StepsStarted[0];
        Assert.Equal(agent.Id.ToString(), started.AgentId);
        Assert.Equal("Researcher", started.AgentRole);
        Assert.Equal(task.Id.ToString(), started.TaskId);
        Assert.Equal("3 rows", handler.StepsCompleted[0].Observation);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_blocked_or_failing_tool_call_completes_its_step_as_failed()
    {
        var shell = new SpyTool("shell");
        var broken = new SpyTool("broken", result: "disk full", succeed: false);
        var provider = new ScriptedFullLlmProvider();
        provider.EnqueueOpenAiToolCall("call-1", "shell", "{\"input\":\"ls\"}");
        provider.EnqueueOpenAiToolCall("call-2", "broken", "{\"input\":\"x\"}");
        provider.EnqueueText("Could not.");
        var (callbacks, handler) = Callbacks();

        await NativeLoop(provider, [shell, broken], callbacks, new ToolInvocationPipeline(FakeGuardianPipeline.BlockingTool("shell")))
            .ExecuteAsync(Invocation(BuildAgent(shell, broken), BuildTask()), 5, TestContext.Current.CancellationToken);

        Assert.Empty(shell.Calls);
        Assert.Equal(
            ["StepStarted:tool:shell", "StepCompleted:tool:shell:False",
             "StepStarted:tool:broken", "StepCompleted:tool:broken:False"],
            handler.Hooks);
        Assert.StartsWith("Error: Blocked by Guardian", handler.StepsCompleted[0].Observation, StringComparison.Ordinal);
        Assert.Equal("Error: disk full", handler.StepsCompleted[1].Observation);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_throwing_tool_completes_its_step_as_failed_and_the_exception_still_propagates()
    {
        var tool = new SpyTool("explode", exceptionToThrow: new InvalidOperationException("kaboom"));
        var (callbacks, handler) = Callbacks();
        var pipeline = StepNotifyingToolInvocationPipeline.Wrap(ToolInvocationPipeline.Unguarded, callbacks);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.InvokeAsync(
            new Orkeon.Application.Interfaces.Security.ToolInvocation(
                tool, new Dictionary<string, object?>(), new Orkeon.Application.Interfaces.Security.ToolInvocationCaller("a-1", "Researcher", "t-1")),
            TestContext.Current.CancellationToken));

        Assert.Equal("kaboom", thrown.Message);
        Assert.Equal(["StepStarted:tool:explode", "StepCompleted:tool:explode:False"], handler.Hooks);
        Assert.Equal("Error: kaboom", handler.StepsCompleted[0].Observation);
    }

    [Fact]
    public void Wrap_leaves_the_pipeline_alone_without_callbacks_and_never_wraps_twice()
    {
        var (callbacks, _) = Callbacks();

        Assert.Same(ToolInvocationPipeline.Unguarded, StepNotifyingToolInvocationPipeline.Wrap(ToolInvocationPipeline.Unguarded, null));
        var wrapped = StepNotifyingToolInvocationPipeline.Wrap(ToolInvocationPipeline.Unguarded, callbacks);
        Assert.Same(wrapped, StepNotifyingToolInvocationPipeline.Wrap(wrapped, callbacks));
    }

    [Fact]
    public async System.Threading.Tasks.Task The_orchestrator_reports_the_steps_of_its_text_loop_when_given_callbacks()
    {
        var tool = new SpyTool("web_scrape", result: "page text");
        var provider = new ScriptedBasicLlmProvider();
        provider.Enqueue("""[TOOL_CALL]{tool => "web_scrape", args => {--input "https://example.com"}}[/TOOL_CALL]""");
        provider.Enqueue("A summary.");
        var (callbacks, handler) = Callbacks();
        var orchestrator = new ExecutionOrchestrator(NullLogger<ExecutionOrchestrator>.Instance, provider, new NullPlanner())
        {
            Callbacks = callbacks,
        };

        var result = await orchestrator.ExecuteTaskCoreAsync(BuildAgent(tool), BuildTask(), BuildContext(), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Equal(["StepStarted:tool:web_scrape", "StepCompleted:tool:web_scrape:True"], handler.Hooks);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_completed_task_reports_the_iterations_of_its_loop_as_its_steps()
    {
        // GAP-21: one tool call then the answer are two turns of the text loop, and the task's
        // completion says so — StepsExecuted used to be 1 whatever the loop did.
        var tool = new SpyTool("web_scrape", result: "page text");
        var provider = new ScriptedBasicLlmProvider();
        provider.Enqueue("""[TOOL_CALL]{tool => "web_scrape", args => {--input "https://example.com"}}[/TOOL_CALL]""");
        provider.Enqueue("A summary.");
        var (callbacks, handler) = Callbacks();
        var orchestrator = new ExecutionOrchestrator(NullLogger<ExecutionOrchestrator>.Instance, provider, new NullPlanner())
        {
            Callbacks = callbacks,
        };
        var service = new Orkeon.Application.Agent.AgentExecutionService(
            NullLogger<Orkeon.Application.Agent.AgentExecutionService>.Instance,
            orchestrator,
            callbacks,
            new Orkeon.Application.Tests.Services.TestMemoryCoordinator());

        var result = await service.ExecuteTaskAsync(BuildAgent(tool), BuildTask(), BuildContext(), TestContext.Current.CancellationToken);

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, result.IterationsUsed);
        Assert.Equal(2, Assert.Single(handler.TasksCompleted).StepsExecuted);
    }

    private sealed class NullPlanner : IAgentPlanner
    {
        public System.Threading.Tasks.Task<TaskPlan> CreatePlanAsync(DomainTask task, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public System.Threading.Tasks.Task<TaskPlan> RefinePlanAsync(TaskPlan plan, PlanFeedback feedback, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public System.Threading.Tasks.Task<PlanValidationResult> ValidatePlanAsync(TaskPlan plan, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
