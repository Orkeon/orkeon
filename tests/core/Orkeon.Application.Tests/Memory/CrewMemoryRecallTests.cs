using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Agent;
using Orkeon.Application.Constants.Orchestration;
using Orkeon.Application.Context;
using Orkeon.Application.Crew;
using Orkeon.Application.Execution;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Memory;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Memory;
using DomainTask = Orkeon.Domain.Task.CrewTask;

namespace Orkeon.Application.Tests.Memory;

/// <summary>
/// GAP-30 — a crew with <c>memory: true</c> remembers its runs and uses them: the second run of a
/// named crew finds, in the prompt of the same task, the output of the first, under the memory
/// header. The pieces are the real ones — <see cref="AgentExecutionService"/>,
/// <see cref="ExecutionOrchestrator"/> and its prompt, <see cref="MemoryCoordinator"/>,
/// <see cref="MemoryService"/> over the In-Memory provider — and the model is a recorder.
/// </summary>
public sealed class CrewMemoryRecallTests : IDisposable
{
    private const string Crew = "news-desk";

    private readonly InMemoryProvider _store = new();
    private readonly CrewMemoryProviderRegistry _registry = new();
    private readonly MockEmbeddingProvider _embedder = new();
    private readonly MemoryService _memory;
    private readonly RecordingLlm _llm = new();
    private readonly DomainAgent _analyst = new AgentBuilder().Role("Analyst").Goal("Watch the news").Build();

    public CrewMemoryRecallTests()
    {
        _embedder.SetEmbeddingFunc(LexicalVectors.Of);
        _memory = new MemoryService(new StubMemoryProviderFactory(_store), NullLogger<MemoryService>.Instance, _registry, _store);
    }

    public void Dispose() => _memory.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A kickoff: a fresh crew id, the crew's name, memory on or off.</summary>
    private CrewId Run(string name = Crew, bool memory = true)
    {
        var crewId = CrewId.Create();
        _registry.Record(crewId, providerType: null, name, memory);
        return crewId;
    }

    private AgentExecutionService Execution() => new(
        NullLogger<AgentExecutionService>.Instance,
        new ExecutionOrchestrator(NullLogger<ExecutionOrchestrator>.Instance, _llm, new NullPlanner()),
        new CallbackOrchestrator(NullLogger<CallbackOrchestrator>.Instance),
        // Bag-of-words vectors have their own scale: the default MinScore is the local model's.
        new MemoryCoordinator(
            NullLogger<MemoryCoordinator>.Instance, _memory, _registry, _embedder,
            Options.Create(new CrewMemoryOptions { MinScore = 0.3f })));

    private static SimpleExecutionContext Context(CrewId crewId) => new(crewId, [], NullMemoryScope.Instance, []);

    private static DomainTask Task(string description, string expectedOutput) =>
        DomainTask.Create(TaskDescription.From(description), ExpectedOutput.From(expectedOutput));

    private static DomainTask NewsSummary() =>
        Task("Summarize this week's AI news for the newsletter", "Five bullet points about the AI news");

    [Fact]
    public async System.Threading.Tasks.Task The_same_task_in_a_later_run_reads_what_the_earlier_run_produced()
    {
        _llm.Answer = "Week 39 AI news: chips, regulation and open models.";
        var first = await Execution().ExecuteTaskAsync(_analyst, NewsSummary(), Context(Run()), Ct);
        Assert.True(first.Success, first.Error);

        _llm.Answer = "Week 40 AI news: agents everywhere.";
        await Execution().ExecuteTaskAsync(_analyst, NewsSummary(), Context(Run()), Ct);

        var prompt = _llm.Prompts[^1];
        Assert.Contains(PromptDefaults.MemoriesHeader, prompt, StringComparison.Ordinal);
        Assert.Contains("Week 39 AI news: chips, regulation and open models.", prompt, StringComparison.Ordinal);
        Assert.Contains("· Analyst · Summarize this week's AI news for the newsletter ---", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task An_unrelated_task_in_a_later_run_has_no_memory_section()
    {
        _llm.Answer = "Week 39 AI news: chips, regulation and open models.";
        await Execution().ExecuteTaskAsync(_analyst, NewsSummary(), Context(Run()), Ct);

        await Execution().ExecuteTaskAsync(
            _analyst, Task("Write a SQL migration adding an index on orders", "A migration script"), Context(Run()), Ct);

        Assert.DoesNotContain(PromptDefaults.MemoriesHeader, _llm.Prompts[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_crew_of_another_name_reads_nothing_of_it()
    {
        _llm.Answer = "Week 39 AI news: chips, regulation and open models.";
        await Execution().ExecuteTaskAsync(_analyst, NewsSummary(), Context(Run()), Ct);

        await Execution().ExecuteTaskAsync(_analyst, NewsSummary(), Context(Run("market-desk")), Ct);

        Assert.DoesNotContain(PromptDefaults.MemoriesHeader, _llm.Prompts[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_crew_without_memory_neither_stores_nor_recalls()
    {
        _llm.Answer = "Week 39 AI news: chips, regulation and open models.";
        await Execution().ExecuteTaskAsync(_analyst, NewsSummary(), Context(Run(memory: false)), Ct);
        await Execution().ExecuteTaskAsync(_analyst, NewsSummary(), Context(Run(memory: false)), Ct);

        Assert.DoesNotContain(PromptDefaults.MemoriesHeader, _llm.Prompts[^1], StringComparison.Ordinal);
        Assert.Empty(await _store.SearchAsync(string.Empty, 10, cancellationToken: Ct));
        Assert.Equal(0, _embedder.GetEmbeddingCallCount);
    }

    /// <summary>Records every prompt and answers with <see cref="Answer"/>.</summary>
    private sealed class RecordingLlm : IBasicLlmProvider
    {
        public string Name => "recording";

        public string Answer { get; set; } = "Final answer.";

        public List<string> Prompts { get; } = [];

        public System.Threading.Tasks.Task<string> ChatAsync(
            string message, LlmConfig? config = null, CancellationToken cancellationToken = default)
        {
            Prompts.Add(message);
            return System.Threading.Tasks.Task.FromResult(Answer);
        }

        public System.Threading.Tasks.Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(true);
    }

    private sealed class NullPlanner : Orkeon.Domain.Crew.Planning.IAgentPlanner
    {
        public System.Threading.Tasks.Task<Orkeon.Domain.Crew.Planning.TaskPlan> CreatePlanAsync(
            DomainTask task, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(new Orkeon.Domain.Crew.Planning.TaskPlan());

        public System.Threading.Tasks.Task<Orkeon.Domain.Crew.Planning.TaskPlan> RefinePlanAsync(
            Orkeon.Domain.Crew.Planning.TaskPlan plan,
            Orkeon.Domain.Crew.Planning.PlanFeedback feedback,
            CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(plan);

        public System.Threading.Tasks.Task<Orkeon.Domain.Crew.Planning.PlanValidationResult> ValidatePlanAsync(
            Orkeon.Domain.Crew.Planning.TaskPlan plan, CancellationToken cancellationToken = default) =>
            System.Threading.Tasks.Task.FromResult(new Orkeon.Domain.Crew.Planning.PlanValidationResult { IsValid = true });
    }
}
