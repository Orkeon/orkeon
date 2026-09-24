using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Context;
using Orkeon.Application.Evaluation;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Flows.ValueObjects;
using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Agent;
using Orkeon.Infrastructure.Crew;
using Orkeon.Infrastructure.Evaluation.LlmJudge;
using Orkeon.Infrastructure.Flows.Steps;
using Orkeon.Infrastructure.LLMs;
using Orkeon.Infrastructure.LLMs.Adapters;
using Orkeon.Infrastructure.Memory.Cognitive;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Orkeon.Tests.Shared.FileSystem;
using AppTaskOutput = Orkeon.Application.Execution.TaskOutput;
using DomainAgent = Orkeon.Domain.Agent.Agent;

namespace Orkeon.Infrastructure.Tests.LLMs;

/// <summary>
/// STUDIO-42, the families the meter used to miss: the hierarchical manager, the flows, the
/// memory services, the LLM judges and the streamed agent. For each one, a provider that
/// counts its calls under the one metering decorator: as many usage events as calls, never a
/// duplicate — and each tagged with the work it was (D-02).
/// </summary>
public sealed class GenerationCallFamiliesMeteringTests
{
    private static LlmResponse Counted(string content) =>
        new() { Content = content, PromptTokens = 50, CompletionTokens = 5, TokensUsed = 55 };

    private static CrewTask BuildTask() =>
        new CrewTaskBuilder().Description("Metered task").ExpectedOutput("An answer").Build();

    private static DomainAgent BuildAgent(string role) =>
        new AgentBuilder().Role(role).Goal("Spend tokens").Build();

    private static SimpleExecutionContext BuildContext() =>
        new(CrewId.Create(), [], NullMemoryScope.Instance, []);

    [Fact]
    public async Task The_manager_is_metered_for_each_assignment_and_each_review()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(Counted("""{"agent_id": "unknown", "reason": "any"}"""));
        var sink = new MockLlmUsageSink();
        var metered = MeteredLlmProvider.Wrap(provider, sink);
        using var chatClient = new LlmProviderToChatClientAdapter(metered);
        var manager = new LlmBasedManager(NullLogger<LlmBasedManager>.Instance, new LlmProviderAdapter(metered), chatClient);
        var task = BuildTask();
        var context = BuildContext();

        using (LlmUsageScope.Begin(crewId: context.CrewId.ToString()))
        {
            await manager.AssignTaskAsync(task, [BuildAgent("Writer"), BuildAgent("Reviewer")], context);
            await manager.ReviewOutputAsync(
                new AppTaskOutput(task.Id.ToString(), null, "draft", DateTime.UtcNow, true, TimeSpan.Zero), task);
        }

        Assert.Equal(2, provider.ChatCallCount);
        Assert.Equal(provider.ChatCallCount, sink.Recorded.Count);
        Assert.All(sink.Recorded, usage =>
        {
            Assert.Equal(LlmUsageOperations.Manager, usage.OperationType);
            Assert.Equal(task.Id.ToString(), usage.TaskId);
            Assert.Equal(context.CrewId.ToString(), usage.CrewId);
        });
    }

    [Fact]
    public async Task A_flow_step_is_metered_as_flow_work()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(Counted("42"));
        var sink = new MockLlmUsageSink();
        using var chatClient = new LlmProviderToChatClientAdapter(MeteredLlmProvider.Wrap(provider, sink));
        var step = new LlmFlowStep("ask", FlowStepParameters.Empty.Set("prompt_template", "What is {question}?"), chatClient);

        var result = await step.ExecuteAsync(FlowState.Empty.Set("question", "six times seven"), TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var usage = Assert.Single(sink.Recorded);
        Assert.Equal(LlmUsageOperations.Flow, usage.OperationType);
        Assert.Equal(1, provider.ChatCallCount);
    }

    [Fact]
    public async Task The_memory_services_are_metered_as_memory_work_for_the_agent_that_remembers()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(Counted("""{"has_contradiction": false, "importance": 0.5, "category": "fact"}"""));
        var sink = new MockLlmUsageSink();
        var metered = MeteredLlmProvider.Wrap(provider, sink);
        var options = Options.Create(new CognitiveMemoryOptions());
        var memory = new MockMemoryProvider();
        var ct = TestContext.Current.CancellationToken;
        var similar = new List<MemoryItem>();
        for (var i = 0; i < 5; i++)
        {
            var item = MemoryItem.Create($"Similar content {i}", embedding: [1.0f, 0.0f, 0.0f], importance: 0.5f);
            similar.Add(item);
            await memory.StoreAsync(item.Id, item, ct);
        }

        using (LlmUsageScope.Begin(LlmUsageOperations.Agent, agentId: "Archivist", taskId: "task-7"))
        {
            await new ContradictionDetector(metered, options, new MockLogger<ContradictionDetector>())
                .CheckAsync("the sky is green", [MemoryItem.Create("the sky is blue")], ct);
            await new MemoryAnalyzer(metered, options, new MockLogger<MemoryAnalyzer>())
                .AnalyzeAsync("remember this", context: null, ct);
            await new MemoryConsolidator(metered, memory, options, new MockLogger<MemoryConsolidator>())
                .ConsolidateAsync(similar, ct);
        }

        Assert.True(provider.ChatCallCount >= 3);
        Assert.Equal(provider.ChatCallCount, sink.Recorded.Count);
        Assert.All(sink.Recorded, usage =>
        {
            Assert.Equal(LlmUsageOperations.Memory, usage.OperationType);
            Assert.Equal("Archivist", usage.AgentId);
            Assert.Equal("task-7", usage.TaskId);
        });
    }

    [Fact]
    public async Task An_llm_judge_is_metered_as_judge_work()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(Counted("""{"score": 8, "reasoning": "clear"}"""));
        var sink = new MockLlmUsageSink();
        using var chatClient = new LlmProviderToChatClientAdapter(MeteredLlmProvider.Wrap(provider, sink));

        var score = await new CoherenceEvaluator(chatClient).EvaluateAsync(new EvaluationInput("an output to grade"), TestContext.Current.CancellationToken);

        Assert.True(score.Score > 0);
        var usage = Assert.Single(sink.Recorded);
        Assert.Equal(LlmUsageOperations.Judge, usage.OperationType);
    }

    [Fact]
    public async Task A_streamed_agent_turn_is_metered_once_for_its_agent_its_task_and_its_crew()
    {
        // The streamed path has no usage channel (D-06): the turn is counted once, at the end
        // of the stream, as an estimate — attributed like any other agent turn.
        var provider = new MockStreamingLlmProvider { SupportsStreaming = true };
        provider.SetStreamingChunks(["A streamed ", "final answer"]);
        var sink = new MockLlmUsageSink();
        using var chatClient = new LlmProviderToChatClientAdapter(MeteredLlmProvider.Wrap(provider, sink));
        var service = new StreamingAgentExecutionService(
            chatClient, [], NullLogger<StreamingAgentExecutionService>.Instance, new FakeFileSystemService());
        var task = BuildTask();
        var context = BuildContext();

        await foreach (var _ in service.StreamExecutionAsync(BuildAgent("Streamer"), task, context, TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal(1, provider.GenerateStreamingCallCount);
        var usage = Assert.Single(sink.Recorded);
        Assert.True(usage.Estimated);
        Assert.Equal(LlmUsageOperations.Agent, usage.OperationType);
        Assert.Equal("Streamer", usage.AgentId);
        Assert.Equal(task.Id.ToString(), usage.TaskId);
        Assert.Equal(context.CrewId.ToString(), usage.CrewId);
    }
}
