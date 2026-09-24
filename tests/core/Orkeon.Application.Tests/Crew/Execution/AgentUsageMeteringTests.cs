using DomainAgent = Orkeon.Domain.Agent.Agent;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Crew.Planning;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Domain.Tools;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Tests.Shared.FileSystem;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// STUDIO-42, the agent families: every call an agent's task makes — its turns, the retry of
/// an empty answer, the synthesis once the iterations run out, the fallback loops without a
/// chat client, the correction round — reaches the meter exactly once, attributed to that
/// agent, that task, that crew. The provider is metered the way the factory meters every
/// provider; the orchestrator only says whose calls they are.
/// </summary>
public sealed class AgentUsageMeteringTests
{
    private const string Role = "Metered Agent";

    private static DomainAgent BuildAgent(int maxIterations = 5, params ITool[] tools)
    {
        var builder = new AgentBuilder().Role(Role).Goal("Spend tokens").MaxIterations(maxIterations);
        foreach (var tool in tools)
            builder = builder.WithTool(tool);
        return builder.Build();
    }

    private static DomainTask BuildTask() =>
        DomainTask.Create(TaskDescription.From("Metered task"), ExpectedOutput.From("An answer"));

    private static SimpleExecutionContext BuildContext() =>
        new(CrewId.Create(), [], NullMemoryScope.Instance, []);

    private static LlmResponse Counted(LlmResponse response) => response with
    {
        PromptTokens = 100,
        CompletionTokens = 10,
        TokensUsed = 110,
    };

    /// <summary>Every event is the agent's, on its task, in its crew.</summary>
    private static void AssertAttributedTo(
        IReadOnlyList<CostUsageEvent> events, DomainTask task, SimpleExecutionContext context) =>
        Assert.All(events, usage =>
        {
            Assert.Equal(LlmUsageOperations.Agent, usage.OperationType);
            Assert.Equal(Role, usage.AgentId);
            Assert.Equal(task.Id.ToString(), usage.TaskId);
            Assert.Equal(context.CrewId.ToString(), usage.CrewId);
        });

    [Fact]
    public async System.Threading.Tasks.Task A_tool_turn_and_the_retry_of_an_empty_answer_are_each_counted_once()
    {
        var tool = new SpyTool("worker", result: "did work");
        var provider = new ScriptedFullLlmProvider();
        provider.EnqueueOpenAiToolCall("call-1", "worker", "{\"input\":\"x\"}");
        provider.Enqueue(Counted(new LlmResponse { Content = "" }));
        provider.Enqueue(Counted(new LlmResponse { Content = "the retried answer" }));
        var sink = new MockLlmUsageSink();
        var metered = MeteredLlmProvider.Wrap(provider, sink);
        using var chatClient = new LlmProviderToChatClientAdapter(metered);
        var orchestrator = new ExecutionOrchestrator(
            NullLogger<ExecutionOrchestrator>.Instance, new LlmProviderAdapter(metered), new NullAgentPlanner(),
            chatClient, [tool], new FakeFileSystemService());
        var task = BuildTask();
        var context = BuildContext();

        var result = await orchestrator.ExecuteTaskCoreAsync(BuildAgent(5, tool), task, context, TestContext.Current.CancellationToken);

        Assert.Equal("the retried answer", result.Output);
        Assert.Equal(3, provider.ReceivedTurns.Count);
        Assert.Equal(provider.ReceivedTurns.Count, sink.Recorded.Count);
        AssertAttributedTo(sink.Recorded, task, context);
    }

    [Fact]
    public async System.Threading.Tasks.Task The_synthesis_asked_for_once_the_iterations_ran_out_is_counted()
    {
        var tool = new SpyTool("worker");
        var provider = new ScriptedFullLlmProvider();
        provider.EnqueueOpenAiToolCall("call-1", "worker", "{\"input\":\"x\"}");
        provider.Enqueue(Counted(new LlmResponse { Content = "synthesized from what was gathered" }));
        var sink = new MockLlmUsageSink();
        var metered = MeteredLlmProvider.Wrap(provider, sink);
        using var chatClient = new LlmProviderToChatClientAdapter(metered);
        var orchestrator = new ExecutionOrchestrator(
            NullLogger<ExecutionOrchestrator>.Instance, new LlmProviderAdapter(metered), new NullAgentPlanner(),
            chatClient, [tool], new FakeFileSystemService());
        var task = BuildTask();
        var context = BuildContext();

        var result = await orchestrator.ExecuteTaskCoreAsync(BuildAgent(1, tool), task, context, TestContext.Current.CancellationToken);

        Assert.Equal("synthesized from what was gathered", result.Output);
        Assert.Equal(2, sink.Recorded.Count);
        Assert.Equal(provider.ReceivedTurns.Count, sink.Recorded.Count);
        AssertAttributedTo(sink.Recorded, task, context);
    }

    [Fact]
    public async System.Threading.Tasks.Task The_text_loop_without_a_chat_client_is_counted_for_its_agent()
    {
        var provider = new ScriptedFullLlmProvider();
        provider.Enqueue(Counted(new LlmResponse { Content = "legacy answer" }));
        var sink = new MockLlmUsageSink();
        var orchestrator = new ExecutionOrchestrator(
            NullLogger<ExecutionOrchestrator>.Instance,
            new LlmProviderAdapter(MeteredLlmProvider.Wrap(provider, sink)),
            new NullAgentPlanner());
        var task = BuildTask();
        var context = BuildContext();

        await orchestrator.ExecuteTaskCoreAsync(BuildAgent(), task, context, TestContext.Current.CancellationToken);

        var usage = Assert.Single(sink.Recorded);
        Assert.Equal(110, usage.PromptTokens + usage.CompletionTokens);
        AssertAttributedTo(sink.Recorded, task, context);
    }

    [Fact]
    public async System.Threading.Tasks.Task The_native_tool_calling_loop_counts_each_of_its_turns_once()
    {
        var tool = new SpyTool("worker");
        var agent = BuildAgent(5, tool);
        var provider = new ScriptedFullLlmProvider();
        provider.EnqueueOpenAiToolCall("call-1", "worker", "{\"input\":\"x\"}");
        provider.Enqueue(Counted(new LlmResponse { Content = "native answer" }));
        var sink = new MockLlmUsageSink();
        var logger = new SpyExecutionLogger();
        var loop = new NativeToolCallingAgentLoop(
            logger, MeteredLlmProvider.Wrap(provider, sink), new FakeToolCallingStrategy(new OpenAiShapedToolCallParser()),
            [tool], new LlmCallGate(logger, new ScriptedBasicLlmProvider(), rateLimiter: null));

        var result = await loop.ExecuteAsync(
            new ExecutionInvocationContext(agent, BuildTask(), "system", "user", Context: null, ToolsUsed: [], Stopwatch: System.Diagnostics.Stopwatch.StartNew()),
            5, TestContext.Current.CancellationToken);

        Assert.Equal("native answer", result.Output);
        Assert.Equal(2, provider.ReceivedTurns.Count);
        Assert.Equal(provider.ReceivedTurns.Count, sink.Recorded.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task The_single_shot_correction_round_is_counted_for_the_agent()
    {
        var provider = new ScriptedFullLlmProvider();
        provider.Enqueue(Counted(new LlmResponse { Content = "{\"fixed\": true}" }));
        var sink = new MockLlmUsageSink();
        var pipeline = new ScriptedValidationPipeline(false, true);
        var coordinator = new OutputValidationCoordinator(
            new SpyExecutionLogger(), pipeline, parserFactory: null,
            new LlmProviderAdapter(MeteredLlmProvider.Wrap(provider, sink)), chatLoop: null);
        var task = BuildTask();
        var validation = new OutputValidationContext(ExpectedFormat: Orkeon.Application.Interfaces.Ports.OutputFormat.Json);

        using (LlmUsageScope.Begin(LlmUsageOperations.Agent, agentId: Role, taskId: task.Id.ToString()))
        {
            var (output, _) = await coordinator.ValidateAndParseOutputAsync(
                new OutputValidationRequest("not json", validation, task, BuildAgent(), "system", "user", []),
                maxOutputRetries: 1, defaultMaxIterations: 5, TestContext.Current.CancellationToken);
            Assert.Equal("{\"fixed\": true}", output);
        }

        var usage = Assert.Single(sink.Recorded);
        Assert.Equal(LlmUsageOperations.Agent, usage.OperationType);
        Assert.Equal(Role, usage.AgentId);
    }

    /// <summary>A planner with nothing to say: the tests exercise execution, not planning.</summary>
    private sealed class NullAgentPlanner : IAgentPlanner
    {
        public System.Threading.Tasks.Task<TaskPlan> CreatePlanAsync(DomainTask task, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(new TaskPlan { TaskId = task.Id, Steps = [] });

        public System.Threading.Tasks.Task<TaskPlan> RefinePlanAsync(TaskPlan plan, PlanFeedback feedback, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(plan);

        public System.Threading.Tasks.Task<PlanValidationResult> ValidatePlanAsync(TaskPlan plan, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(new PlanValidationResult { IsValid = true });
    }

    /// <summary>Answers the validation verdicts it was given, in order.</summary>
    private sealed class ScriptedValidationPipeline(params bool[] valid) : IOutputValidationPipeline
    {
        private int _next;

        public System.Threading.Tasks.Task<OutputPipelineResult> ValidateAsync(
            string output, OutputValidationContext context, CancellationToken ct = default)
        {
            var isValid = valid[Math.Min(_next++, valid.Length - 1)];
            return System.Threading.Tasks.Task.FromResult(isValid
                ? new OutputPipelineResult(true, [])
                : new OutputPipelineResult(false, [new OutputValidationResult(false, "not valid JSON", "Wrap it in braces", "scripted")], "not valid JSON"));
        }

        public IOutputValidationPipeline AddValidator(IOutputValidator validator) => this;
    }
}
