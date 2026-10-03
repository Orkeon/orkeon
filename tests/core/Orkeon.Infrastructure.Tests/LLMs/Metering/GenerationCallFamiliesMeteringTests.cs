using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Context;
using Orkeon.Application.DependencyInjection;
using Orkeon.Application.Evaluation;
using Orkeon.Application.Interfaces;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.FileSystem;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task;
using Orkeon.Infrastructure.Crew;
using Orkeon.Infrastructure.Evaluation.LlmJudge;
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
/// memory services, the LLM judges and the streamed agent turn. For each one, a provider that
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
        var manager = new LlmBasedManager(NullLogger<LlmBasedManager>.Instance);
        var llm = new ManagerLlm { ChatClient = chatClient, Name = "profile:default" };
        var task = BuildTask();
        var context = BuildContext();

        using (LlmUsageScope.Begin(crewId: context.CrewId.ToString()))
        {
            await manager.AssignTaskAsync(task, [BuildAgent("Writer"), BuildAgent("Reviewer")], context, llm);
            await manager.ReviewOutputAsync(
                new AppTaskOutput(task.Id.ToString(), null, "draft", DateTime.UtcNow, true, TimeSpan.Zero), task, llm);
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
    public async Task The_memory_services_are_metered_as_memory_work_for_the_agent_that_remembers()
    {
        var provider = new MockLlmProvider();
        provider.SetChatResult(Counted("""{"has_contradiction": false, "importance": 0.5, "category": "fact"}"""));
        var sink = new MockLlmUsageSink();
        var metered = MeteredLlmProvider.Wrap(provider, sink);
        var options = Options.Create(new CognitiveMemoryOptions());
        var memory = new RecordingLongTermMemory();
        var ct = TestContext.Current.CancellationToken;
        var similar = new List<ScoredMemoryItem>();
        for (var i = 0; i < 5; i++)
        {
            var item = MemoryItem.Create($"Similar content {i}", embedding: [1.0f, 0.0f, 0.0f], importance: 0.5f);
            similar.Add(new ScoredMemoryItem(item, 1f, item.Id.ToString()));
            await memory.AddAsync(item);
        }

        using (LlmUsageScope.Begin(LlmUsageOperations.Agent, agentId: "Archivist", taskId: "task-7"))
        {
            await new ContradictionDetector(metered, options, new MockLogger<ContradictionDetector>())
                .CheckAsync("the sky is green", [MemoryItem.Create("the sky is blue")], ct);
            await new MemoryAnalyzer(metered, options, new MockLogger<MemoryAnalyzer>())
                .AnalyzeAsync("remember this", context: null, ct);
            await new MemoryConsolidator(metered, new MockEmbeddingProvider(), options, new MockLogger<MemoryConsolidator>())
                .ConsolidateAsync(similar, memory, ct);
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
        // GAP-32: a streamed agent turn is the crew's own turn, through the chat client's streaming
        // path, which reads the provider's chat stream — its final response carries the usage. It
        // is counted once, attributed like any other turn, with the provider's own figures: the old
        // text-only stream carried no usage and was counted as an estimate.
        var provider = new MockStreamingLlmProvider { SupportsStreaming = true };
        provider.SetStreamingChunks(["A streamed ", "final answer"]);
        provider.SetCompletedResponse(new LlmResponse
        {
            Content = "A streamed final answer",
            PromptTokens = 120,
            CompletionTokens = 8,
            TokensUsed = 128,
        });
        var sink = new MockLlmUsageSink();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IFileSystemService>(new FakeFileSystemService()
            .AddMount("/output", FileAccessRights.Read | FileAccessRights.Write | FileAccessRights.Create));
        services.AddSingleton<ILlmUsageSink>(sink);
        services.AddOrkeonLlmProvider(_ => provider);
        services.AddOrkeonApplication();
        services.AddOrkeonInfrastructure();
        await using var host = services.BuildServiceProvider();
        await using var scope = host.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var config = await sp.GetRequiredService<ICrewDefinitionLoader>().LoadFromStringAsync("""
            name: metered-desk
            goal: Spend tokens
            agents:
              streamer:
                role: Streamer
                goal: Spend tokens
            tasks:
              answer:
                description: Metered task
                expected_output: An answer
                agent: streamer
            """, TestContext.Current.CancellationToken);
        var crew = await sp.GetRequiredService<ICrewFactory>().CreateFromConfigAsync(config, TestContext.Current.CancellationToken);
        var orchestrator = sp.GetRequiredService<Orkeon.Application.Interfaces.Services.ICrewOrchestrationService>();

        await foreach (var _ in orchestrator.KickoffStreamingAsync(
            crew.Id, Orkeon.Application.Interfaces.Services.CrewInput.Empty(), TestContext.Current.CancellationToken))
        {
        }

        Assert.Equal(1, provider.ChatStreamingCallCount);
        Assert.Equal(0, provider.ChatCallCount);
        Assert.Equal(0, provider.GenerateStreamingCallCount);
        var usage = Assert.Single(sink.Recorded);
        Assert.False(usage.Estimated);
        Assert.Equal(120, usage.PromptTokens);
        Assert.Equal(8, usage.CompletionTokens);
        Assert.Equal(LlmUsageOperations.Agent, usage.OperationType);
        Assert.Equal("Streamer", usage.AgentId);
        Assert.Equal(crew.Tasks[0].ToString(), usage.TaskId);
        Assert.Equal(crew.Id.ToString(), usage.CrewId);
    }
}
