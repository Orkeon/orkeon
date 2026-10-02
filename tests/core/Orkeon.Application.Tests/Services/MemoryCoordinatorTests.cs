using DomainAgent = Orkeon.Domain.Agent.Agent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Constants.Orchestration;
using Orkeon.Application.Context;
using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Memory;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Agent.ValueObjects;
using Orkeon.Domain.Common;
using Orkeon.Domain.Task.ValueObjects;
using Orkeon.Infrastructure.Memory;
using DomainTask = Orkeon.Domain.Task.CrewTask;
using TaskOutput = Orkeon.Application.Execution.TaskOutput;

namespace Orkeon.Application.Tests.Services;

/// <summary>
/// GAP-30 — the crew's memory as a run uses it, through <see cref="MemoryCoordinator"/>: only a crew
/// with <c>memory: true</c> stores and recalls; a memory is embedded on its task and its output, and
/// recalled by vector within the crew's scope, bounded, without what the prompt already carries; a
/// memory that cannot work refuses the run before it starts, and one that fails during the run is a
/// warning. The memory is the real one — <see cref="MemoryService"/> over the In-Memory provider,
/// the store of a named crew that declares no provider — and the vectors are bag-of-words.
/// </summary>
public sealed class MemoryCoordinatorTests : IDisposable
{
    private const string Crew = "news-desk";

    private readonly Fixtures.TestLogger<MemoryCoordinator> _logger = new();
    private readonly InMemoryProvider _store = new();
    private readonly CrewMemoryProviderRegistry _registry = new();
    private readonly MockEmbeddingProvider _embedder = new();
    private readonly MemoryService _innerMemory;
    private readonly RecordingMemoryService _memory;
    private readonly CrewId _crewId = CrewId.Create();
    private readonly DomainAgent _analyst = DomainAgent.Create(
        AgentRole.From("Analyst"), AgentGoal.From("Watch the news"), AgentBackstory.From("Reads a lot"));

    public MemoryCoordinatorTests()
    {
        _embedder.SetEmbeddingFunc(LexicalVectors.Of);
        _innerMemory = new MemoryService(new StubMemoryProviderFactory(_store), NullLogger<MemoryService>.Instance, _registry, _store);
        _memory = new RecordingMemoryService(_innerMemory);
    }

    public void Dispose() => _innerMemory.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private void Remembering(string? name = Crew) =>
        _registry.Record(_crewId, providerType: null, name, memoryEnabled: true);

    private MemoryCoordinator Coordinator(CrewMemoryOptions? options = null, IMemoryService? memory = null) =>
        new(_logger, memory ?? _memory, _registry, _embedder, Options.Create(options ?? new CrewMemoryOptions()));

    private SimpleExecutionContext Context(params string[] previousOutputs) =>
        new(_crewId, [], NullMemoryScope.Instance,
            [.. previousOutputs.Select((output, i) => new TaskOutput($"t-{i}", "a-0", output, DateTime.UtcNow, true, TimeSpan.Zero))]);

    private static DomainTask Task(string description, string expectedOutput = "A short answer.") =>
        DomainTask.Create(TaskDescription.From(description), ExpectedOutput.From(expectedOutput));

    private async System.Threading.Tasks.Task<List<Orkeon.Domain.Memory.MemoryItem>> StoredAsync() =>
        [.. await _store.SearchAsync(string.Empty, 100, cancellationToken: Ct)];

    private IEnumerable<string?> Warnings => _logger.LogEntries.Where(e => e.LogLevel == LogLevel.Warning).Select(e => e.Message);

    // ── memory: false ────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task A_crew_without_memory_stores_nothing_recalls_nothing_and_touches_neither_the_embedder_nor_the_memory()
    {
        _registry.Record(_crewId, providerType: null, Crew, memoryEnabled: false);
        var coordinator = Coordinator();
        var task = Task("Summarize this week's AI news");

        await coordinator.StoreTaskResultAsync(_analyst, task, "Five bullets about AI", Context(), Ct);
        var recalled = await coordinator.RecallAsync(_analyst, task, Context(), Ct);

        Assert.Empty(recalled);
        Assert.Equal(0, _embedder.GetEmbeddingCallCount);
        Assert.Empty(_memory.Calls);
        Assert.Empty(await StoredAsync());
    }

    [Fact]
    public async System.Threading.Tasks.Task A_crew_never_recorded_does_not_remember_either()
    {
        // An A2A task, a hand-built context: a fresh crew id no kickoff recorded.
        var coordinator = Coordinator();
        var task = Task("Summarize this week's AI news");

        await coordinator.StoreTaskResultAsync(_analyst, task, "Five bullets about AI", Context(), Ct);
        await coordinator.EnsureReadyAsync(_crewId, Ct);

        Assert.Empty(await coordinator.RecallAsync(_analyst, task, Context(), Ct));
        Assert.Equal(0, _embedder.GetEmbeddingCallCount);
        Assert.Empty(_memory.Calls);
    }

    // ── what is stored ───────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task A_stored_result_is_embedded_on_its_task_and_its_output_and_says_whose_it_is()
    {
        Remembering();

        await Coordinator().StoreTaskResultAsync(
            _analyst, Task("Summarize this week's AI news"), "Five bullets about AI", Context(), Ct);

        var stored = Assert.Single(await StoredAsync());
        Assert.Equal("Five bullets about AI", stored.Content);
        Assert.Equal(LexicalVectors.Of("Summarize this week's AI news\nFive bullets about AI"), stored.Embedding);
        var properties = stored.Metadata.CustomProperties!;
        Assert.Equal(CrewMemoryScope.CrewMemoryKind, properties[CrewMemoryScope.KindProperty]);
        Assert.Equal(Crew, properties[CrewMemoryScope.CrewProperty]);
        Assert.Equal("Analyst", properties["agent_role"]);
        Assert.Equal("Summarize this week's AI news", properties["task_description"]);
        Assert.True(properties.ContainsKey("stored_at"));
        Assert.Contains($"crew:{Crew}", stored.Tags);
        Assert.Equal(_analyst.Id, stored.Metadata.CreatedBy);
    }

    [Fact]
    public async System.Threading.Tasks.Task The_task_description_is_stored_with_the_run_s_variables()
    {
        Remembering();
        var context = Context() with { Variables = new Dictionary<string, string> { ["topic"] = "chips" } };

        await Coordinator().StoreTaskResultAsync(_analyst, Task("Summarize this week's news about {topic}"), "Three bullets", context, Ct);

        var stored = Assert.Single(await StoredAsync());
        Assert.Equal("Summarize this week's news about chips", stored.Metadata.CustomProperties!["task_description"]);
    }

    [Fact]
    public async System.Threading.Tasks.Task An_empty_output_is_not_remembered()
    {
        Remembering();

        await Coordinator().StoreTaskResultAsync(_analyst, Task("Summarize this week's AI news"), "   ", Context(), Ct);

        Assert.Empty(await StoredAsync());
        Assert.Equal(0, _embedder.GetEmbeddingCallCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task An_unnamed_crew_remembers_in_a_store_of_its_own_tagged_with_its_id()
    {
        // Scoped by an id no later run will carry, its memory lasts its run: never the shared store.
        Remembering(name: null);
        var coordinator = Coordinator(new CrewMemoryOptions { MinScore = 0.1f });
        var task = Task("Summarize this week's AI news");

        await coordinator.StoreTaskResultAsync(_analyst, task, "Five bullets about AI news", Context(), Ct);

        Assert.Empty(await StoredAsync());
        var remembered = Assert.Single(await _memory.Inner.GetMemorySystem(_crewId).LongTerm.SearchSimilarAsync(
            LexicalVectors.Of("AI news"), 5, -1f, Ct));
        Assert.Contains($"crew:{_crewId}", remembered.Item.Tags);
    }

    // ── the recall ───────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task A_memory_already_among_the_previous_outputs_is_not_recalled_and_the_recall_still_fills_its_limit()
    {
        Remembering();
        var coordinator = Coordinator(new CrewMemoryOptions { RecallLimit = 2, MinScore = 0.1f });
        var task = Task("Summarize the weekly AI news");
        await coordinator.StoreTaskResultAsync(_analyst, task, "Week 37: the AI news summary", Context(), Ct);
        await coordinator.StoreTaskResultAsync(_analyst, task, "Week 38: the AI news summary", Context(), Ct);
        await coordinator.StoreTaskResultAsync(_analyst, task, "Week 39: the AI news summary", Context(), Ct);

        var recalled = await coordinator.RecallAsync(_analyst, task, Context("Week 39: the AI news summary"), Ct);

        Assert.Equal(2, recalled.Count);
        Assert.DoesNotContain(recalled, memory => memory.Content == "Week 39: the AI news summary");
    }

    [Fact]
    public async System.Threading.Tasks.Task The_recall_keeps_to_its_limit_and_cuts_the_last_memory_to_its_character_budget()
    {
        Remembering();
        var coordinator = Coordinator(new CrewMemoryOptions { RecallLimit = 3, MinScore = 0.1f, MaxChars = 100 });
        var task = Task("Summarize the weekly AI news");
        for (var week = 1; week <= 5; week++)
            await coordinator.StoreTaskResultAsync(_analyst, task, $"Week {week}: AI news " + new string('x', 60), Context(), Ct);

        var recalled = await coordinator.RecallAsync(_analyst, task, Context(), Ct);

        Assert.Equal(2, recalled.Count);
        Assert.Equal(100, recalled.Sum(memory => memory.Content.Length));
        Assert.EndsWith("[truncated]", recalled[1].Content, StringComparison.Ordinal);
        Assert.All(recalled, memory =>
        {
            Assert.Equal("Analyst", memory.AgentRole);
            Assert.Equal("Summarize the weekly AI news", memory.TaskDescription);
        });
    }

    [Fact]
    public async System.Threading.Tasks.Task The_recall_returns_no_more_than_its_limit()
    {
        Remembering();
        var coordinator = Coordinator(new CrewMemoryOptions { RecallLimit = 3, MinScore = 0.1f });
        var task = Task("Summarize the weekly AI news");
        for (var week = 1; week <= 5; week++)
            await coordinator.StoreTaskResultAsync(_analyst, task, $"Week {week}: AI news", Context(), Ct);

        Assert.Equal(3, (await coordinator.RecallAsync(_analyst, task, Context(), Ct)).Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task An_unrelated_task_recalls_nothing()
    {
        Remembering();
        var coordinator = Coordinator(new CrewMemoryOptions { MinScore = 0.3f });
        await coordinator.StoreTaskResultAsync(
            _analyst, Task("Summarize the weekly AI news"), "Chips, regulation and open models", Context(), Ct);

        var recalled = await coordinator.RecallAsync(
            _analyst, Task("Write a SQL migration adding an index", "A migration script"), Context(), Ct);

        Assert.Empty(recalled);
    }

    [Fact]
    public void Each_recalled_memory_gets_one_heading_line_between_the_previous_outputs_and_the_knowledge()
    {
        var storedAt = new DateTime(2026, 9, 30, 8, 0, 0, DateTimeKind.Utc);
        const string longDescription =
            "Summarize the weekly AI news for the team newsletter, in five bullets, for engineers and managers alike";
        var context = Context("An earlier result of this run") with
        {
            RecalledMemories =
            [
                new RecalledMemory(storedAt, "Analyst", longDescription, "Week 39: five bullets"),
                new RecalledMemory(storedAt, "Writer", "Draft the\nnewsletter", "Draft of week 39"),
            ],
        };

        var prompt = AgentPromptComposer.BuildUserPrompt(Task("Summarize the weekly AI news"), context, "Retrieved knowledge block");

        var header = prompt.IndexOf(PromptDefaults.MemoriesHeader, StringComparison.Ordinal);
        Assert.True(prompt.IndexOf(PromptDefaults.PreviousOutputsHeader, StringComparison.Ordinal) < header);
        Assert.True(header < prompt.IndexOf("Retrieved knowledge block", StringComparison.Ordinal));
        var nl = Environment.NewLine;
        Assert.Contains($"--- 2026-09-30 · Analyst · {longDescription[..79]}… ---{nl}Week 39: five bullets{nl}", prompt, StringComparison.Ordinal);
        Assert.Contains($"--- 2026-09-30 · Writer · Draft the newsletter ---{nl}Draft of week 39{nl}", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void A_context_without_memories_has_no_memory_section()
    {
        var prompt = AgentPromptComposer.BuildUserPrompt(Task("Summarize the weekly AI news"), Context("An earlier result"));

        Assert.DoesNotContain(PromptDefaults.MemoriesHeader, prompt, StringComparison.Ordinal);
    }

    // ── readiness ────────────────────────────────────────────────────────

    [Fact]
    public async System.Threading.Tasks.Task EnsureReady_on_an_embedder_that_fails_names_the_crew_and_the_cause()
    {
        Remembering();
        _embedder.SetEmbeddingFunc(_ => throw new InvalidOperationException("the embedding model is not loaded"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator().EnsureReadyAsync(_crewId, Ct));

        Assert.Contains(Crew, error.Message, StringComparison.Ordinal);
        Assert.Contains("the embedding model is not loaded", error.Message, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Embeddings", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task EnsureReady_without_an_embedder_names_the_crew_and_the_remedy()
    {
        Remembering();
        var coordinator = new MemoryCoordinator(_logger, _memory, _registry, embeddingProvider: null);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.EnsureReadyAsync(_crewId, Ct));

        Assert.Contains(Crew, error.Message, StringComparison.Ordinal);
        Assert.Contains("Orkeon:Embeddings", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task EnsureReady_on_a_store_that_cannot_be_searched_names_the_crew_and_the_cause()
    {
        Remembering();
        var down = new ThrowingMemoryProvider(new InvalidOperationException("connection refused"));
        using var memory = new MemoryService(new StubMemoryProviderFactory(down), NullLogger<MemoryService>.Instance, _registry, down);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Coordinator(memory: memory).EnsureReadyAsync(_crewId, Ct));

        Assert.Contains(Crew, error.Message, StringComparison.Ordinal);
        Assert.Contains("connection refused", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task EnsureReady_embeds_a_probe_and_searches_the_crew_memory_once()
    {
        Remembering();

        await Coordinator().EnsureReadyAsync(_crewId, Ct);

        Assert.Equal(1, _embedder.GetEmbeddingCallCount);
        Assert.Equal(nameof(RecordingMemoryService.GetMemorySystem), Assert.Single(_memory.Calls));
    }

    [Fact]
    public async System.Threading.Tasks.Task EnsureReady_of_a_crew_without_memory_touches_nothing()
    {
        _registry.Record(_crewId, providerType: null, Crew, memoryEnabled: false);
        _embedder.SetEmbeddingFunc(_ => throw new InvalidOperationException("never asked"));

        await Coordinator().EnsureReadyAsync(_crewId, Ct);

        Assert.Equal(0, _embedder.GetEmbeddingCallCount);
        Assert.Empty(_memory.Calls);
    }

    // ── a memory that fails during the run is a warning ─────────────────

    [Fact]
    public async System.Threading.Tasks.Task A_store_that_fails_is_a_warning_naming_the_crew_the_task_the_agent_and_the_cause()
    {
        Remembering();
        var down = new ThrowingMemoryProvider(new InvalidOperationException("disk full"), searches: false);
        using var memory = new MemoryService(new StubMemoryProviderFactory(down), NullLogger<MemoryService>.Instance, _registry, down);
        var task = Task("Summarize this week's AI news");

        await Coordinator(memory: memory).StoreTaskResultAsync(_analyst, task, "Five bullets", Context(), Ct);

        var warning = Assert.Single(Warnings);
        Assert.Contains(Crew, warning, StringComparison.Ordinal);
        Assert.Contains(task.Id.ToString(), warning, StringComparison.Ordinal);
        Assert.Contains("Analyst", warning, StringComparison.Ordinal);
        Assert.Contains("disk full", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_recall_that_fails_is_a_warning_and_recalls_nothing()
    {
        Remembering();
        var down = new ThrowingMemoryProvider(new InvalidOperationException("search timed out"), stores: false);
        using var memory = new MemoryService(new StubMemoryProviderFactory(down), NullLogger<MemoryService>.Instance, _registry, down);

        var recalled = await Coordinator(memory: memory).RecallAsync(_analyst, Task("Summarize this week's AI news"), Context(), Ct);

        Assert.Empty(recalled);
        Assert.Contains("search timed out", Assert.Single(Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_cancelled_recall_is_not_swallowed()
    {
        Remembering();
        _embedder.SetEmbeddingFunc(_ => throw new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Coordinator().RecallAsync(_analyst, Task("Summarize this week's AI news"), Context(), Ct));
        Assert.Empty(Warnings);
    }

    // ── arguments ────────────────────────────────────────────────────────

    [Fact]
    public void The_constructor_refuses_a_missing_collaborator()
    {
        Assert.Equal("logger", Assert.Throws<ArgumentNullException>(() => new MemoryCoordinator(null!, _memory, _registry)).ParamName);
        Assert.Equal("memoryService", Assert.Throws<ArgumentNullException>(() => new MemoryCoordinator(_logger, null!, _registry)).ParamName);
        Assert.Equal("providerRegistry", Assert.Throws<ArgumentNullException>(() => new MemoryCoordinator(_logger, _memory, null!)).ParamName);
    }

    [Fact]
    public async System.Threading.Tasks.Task The_methods_refuse_a_missing_argument()
    {
        var coordinator = Coordinator();
        var task = Task("Summarize this week's AI news");

        await Assert.ThrowsAsync<ArgumentNullException>(() => coordinator.EnsureReadyAsync(null!, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => coordinator.RecallAsync(null!, task, Context(), Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => coordinator.RecallAsync(_analyst, null!, Context(), Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => coordinator.RecallAsync(_analyst, task, null!, Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => coordinator.StoreTaskResultAsync(null!, task, "out", Context(), Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => coordinator.StoreTaskResultAsync(_analyst, null!, "out", Context(), Ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => coordinator.StoreTaskResultAsync(_analyst, task, "out", null!, Ct));
    }
}
