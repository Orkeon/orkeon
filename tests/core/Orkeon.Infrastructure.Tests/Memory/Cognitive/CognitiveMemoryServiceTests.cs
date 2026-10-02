using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Memory;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Memory.Cognitive;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Infrastructure.Tests.Memory.Cognitive;

/// <summary>
/// The cognitive memory works in the crew's own memory (GAP-30, decisions 6 and 8): the long-term
/// memory <see cref="MemoryService"/> materializes for the crew — here a named crew on the host's
/// default provider, and a crew declaring a provider of its own. It stores a remembered item once,
/// stamped as the crew's; it finds contradiction candidates, recalls and consolidates by
/// similarity within the crew's scope; and it resolves a conflict where its candidates came from.
/// Both stores are real In-Memory providers, recorded.
/// </summary>
public sealed class CognitiveMemoryServiceTests : IDisposable
{
    private const string Crew = "news-desk";
    private static readonly float[] EmbeddingStandard = [0.1f, 0.2f, 0.3f];

    private readonly InMemoryProvider _applicationStore = new();
    private readonly RecordingMemoryProvider _application;
    private readonly InMemoryProvider _declaredStore = new();
    private readonly RecordingMemoryProvider _declared;
    private readonly CrewMemoryProviderRegistry _registry = new();
    private readonly MemoryService _memoryService;
    private readonly MockEmbeddingProvider _embeddingProvider = new();
    private readonly MockLlmProvider _llmProvider = new();
    private readonly CognitiveMemoryService _service;
    private readonly CrewId _crewId = CrewId.Create();

    public CognitiveMemoryServiceTests()
    {
        _application = new RecordingMemoryProvider(_applicationStore);
        _declared = new RecordingMemoryProvider(_declaredStore);
        _memoryService = new MemoryService(
            new StubMemoryProviderFactory(_application, new Dictionary<string, IMemoryProvider> { ["crewstore"] = _declared }),
            NullLogger<MemoryService>.Instance, _registry, _application);

        // What the kickoff recorded: a named crew without a provider of its own.
        _registry.Record(_crewId, providerType: null, Crew, memoryEnabled: true);
        _embeddingProvider.SetEmbeddingResult(EmbeddingStandard);

        var options = Options.Create(new CognitiveMemoryOptions());
        var analysisServices = new CognitiveAnalysisServices(
            new MemoryAnalyzer(_llmProvider, options, new MockLogger<MemoryAnalyzer>()),
            new ContradictionDetector(_llmProvider, options, new MockLogger<ContradictionDetector>()),
            new MemoryConsolidator(_llmProvider, _embeddingProvider, options, new MockLogger<MemoryConsolidator>()),
            new CompositeScorer(options));
        _service = new CognitiveMemoryService(
            _memoryService, _embeddingProvider, analysisServices, options, new MockLogger<CognitiveMemoryService>());
    }

    public void Dispose() => _memoryService.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ILongTermMemory CrewMemory(CrewId crewId) => _memoryService.GetMemorySystem(crewId).LongTerm;

    /// <summary>A memory the crew already holds, stored the way its runs store theirs.</summary>
    private async Task<MemoryItem> SeedAsync(CrewId crewId, string content, float importance = 0.5f)
    {
        var item = MemoryItem.Create(content, embedding: EmbeddingStandard, importance: importance);
        await CrewMemory(crewId).AddAsync(item);
        return item;
    }

    /// <summary>The analysis answer, then the contradiction verdict.</summary>
    private void Answers(string analysisJson, string verdictJson)
    {
        var calls = 0;
        _llmProvider.SetChatFunc((_, _) => new LlmResponse { Content = ++calls == 1 ? analysisJson : verdictJson });
    }

    private static string Analysis(float importance = 0.7f) =>
        $$"""{"importance": {{importance.ToString(System.Globalization.CultureInfo.InvariantCulture)}}, "category": "fact", "key_entities": [], "summary": "A fact", "suggested_tags": ["fact"], "reasoning": "test"}""";

    private static string Verdict(string action, params MemoryItem[] conflicting) =>
        $$"""{"has_contradiction": {{(conflicting.Length > 0 ? "true" : "false")}}, "conflicting_ids": [{{string.Join(",", conflicting.Select(c => $"\"{c.Id}\""))}}], "description": "", "resolution": "", "action": "{{action}}"}""";

    // ── decision 8: stored once ───────────────────────────────────────

    [Fact]
    public async Task RememberAsync_stores_the_enriched_item_once_in_the_crew_memory_stamped_as_the_crew_s()
    {
        Answers(Analysis(0.7f), Verdict("keep_both"));

        var result = await _service.RememberAsync(_crewId, "Test content to remember", "test context", Ct);

        Assert.Equal("Test content to remember", result.Content);
        Assert.Equal(0.7f, result.Importance, 0.01f);
        var key = Assert.Single(_application.Stored);
        var stored = await _applicationStore.GetAsync(key, Ct);
        Assert.Equal(CrewMemoryScope.CrewMemoryKind, stored!.Metadata.CustomProperties![CrewMemoryScope.KindProperty]);
        Assert.Equal(Crew, stored.Metadata.CustomProperties[CrewMemoryScope.CrewProperty]);
        Assert.Equal("fact", stored.Metadata.CustomProperties["category"]);
        Assert.Equal(EmbeddingStandard, stored.Embedding);
    }

    // ── conflicts, resolved in the crew's memory ─────────────────────

    [Fact]
    public async Task RememberAsync_ContradictionKeepNew_removes_the_old_memory_from_the_crew_memory()
    {
        var existing = await SeedAsync(_crewId, "Old content");
        Answers(Analysis(0.8f), Verdict("keep_new", existing));

        var result = await _service.RememberAsync(_crewId, "New content replacing old", cancellationToken: Ct);

        Assert.Equal("New content replacing old", result.Content);
        Assert.Equal(existing.Id.ToString(), Assert.Single(_application.Deleted));
        Assert.Null(await _applicationStore.GetAsync(existing.Id.ToString(), Ct));
        Assert.NotNull(await _applicationStore.GetAsync(result.Id.ToString(), Ct));
    }

    [Fact]
    public async Task RememberAsync_ContradictionKeepExisting_returns_the_existing_memory_and_stores_nothing()
    {
        var existing = await SeedAsync(_crewId, "Existing content", importance: 0.9f);
        Answers(Analysis(0.3f), Verdict("keep_existing", existing));

        var result = await _service.RememberAsync(_crewId, "New less important content", cancellationToken: Ct);

        Assert.Equal("Existing content", result.Content);
        Assert.Single(_application.Stored); // the seed
        Assert.Empty(_application.Deleted);
    }

    [Fact]
    public async Task RememberAsync_ContradictionMerge_replaces_the_conflicting_memory_with_one_merged_embedded_memory()
    {
        var existing = await SeedAsync(_crewId, "Part A of the story", importance: 0.6f);
        Answers(Analysis(0.7f), Verdict("merge", existing));

        var result = await _service.RememberAsync(_crewId, "Part B of the story", cancellationToken: Ct);

        Assert.Contains("Part B of the story", result.Content, StringComparison.Ordinal);
        Assert.Contains("Part A of the story", result.Content, StringComparison.Ordinal);
        Assert.Equal(existing.Id.ToString(), Assert.Single(_application.Deleted));
        var merged = await _applicationStore.GetAsync(result.Id.ToString(), Ct);
        Assert.NotNull(merged!.Embedding);
        Assert.Equal(Crew, merged.Metadata.CustomProperties![CrewMemoryScope.CrewProperty]);
    }

    [Fact]
    public async Task RememberAsync_ContradictionKeepBoth_keeps_both()
    {
        await SeedAsync(_crewId, "Existing fact");
        Answers(Analysis(0.5f), Verdict("keep_both"));

        var result = await _service.RememberAsync(_crewId, "New complementary fact", cancellationToken: Ct);

        Assert.Equal("New complementary fact", result.Content);
        Assert.Empty(_application.Deleted);
        Assert.Equal(2, _application.Stored.Count);
    }

    [Fact]
    public async Task A_conflict_is_resolved_in_the_store_its_candidates_came_from_never_in_the_application_provider()
    {
        // GAP-30, decision 8: the candidates came from the crew's memory and the deletions went to
        // the application provider — another store, where those ids are nobody's.
        var legal = CrewId.Create();
        _registry.Record(legal, "crewstore", "legal-watch", memoryEnabled: true);
        var existing = await SeedAsync(legal, "The deadline is Friday");
        Answers(Analysis(0.8f), Verdict("keep_new", existing));

        await _service.RememberAsync(legal, "The deadline is Monday", cancellationToken: Ct);

        Assert.Equal(existing.Id.ToString(), Assert.Single(_declared.Deleted));
        Assert.Equal(2, _declared.Stored.Count);
        Assert.Empty(_application.Deleted);
        Assert.Empty(_application.Stored);
        Assert.All(_declared.Searches, search => Assert.Equal("legal-watch", search.Filter?[CrewMemoryScope.CrewProperty]));
    }

    // ── decision 6: the crew's scope ─────────────────────────────────

    [Fact]
    public async Task Two_named_crews_each_recall_their_own_memories_never_the_other_s_nor_a_rag_chunk()
    {
        var market = CrewId.Create();
        _registry.Record(market, providerType: null, "market-desk", memoryEnabled: true);
        _llmProvider.SetChatResult(Analysis());
        var rag = MemoryItem.Create("A RAG chunk on the same subject", embedding: EmbeddingStandard,
            customProperties: new Dictionary<string, string> { ["rag.kind"] = "chunk" });
        await _applicationStore.StoreAsync(rag.Id, rag, Ct);

        await _service.RememberAsync(_crewId, "News desk: chips are scarce", cancellationToken: Ct);
        await _service.RememberAsync(market, "Market desk: chips are expensive", cancellationToken: Ct);

        var news = await _service.RecallAsync(_crewId, "chips", cancellationToken: Ct);
        var marketRecall = await _service.RecallAsync(market, "chips", cancellationToken: Ct);

        Assert.Equal("News desk: chips are scarce", Assert.Single(news).Item.Content);
        Assert.Equal("Market desk: chips are expensive", Assert.Single(marketRecall).Item.Content);
    }

    [Fact]
    public async Task RecallAsync_returns_the_crew_memories_composite_scored()
    {
        await SeedAsync(_crewId, "Relevant content", importance: 0.8f);
        await SeedAsync(_crewId, "Somewhat relevant", importance: 0.4f);

        var results = await _service.RecallAsync(_crewId, "search query", cancellationToken: Ct);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].CompositeScore >= results[1].CompositeScore);
        Assert.All(_application.Searches, search =>
        {
            Assert.Equal(nameof(IMemoryProvider.SearchSimilarAsync), search.Method);
            Assert.Equal(Crew, search.Filter?[CrewMemoryScope.CrewProperty]);
        });
    }

    // ── decision 8: consolidation by similarity ──────────────────────

    [Fact]
    public async Task Consolidation_finds_the_crew_memories_by_similarity_in_its_scope_never_by_an_empty_text_search()
    {
        for (var i = 0; i < 5; i++)
            await SeedAsync(_crewId, $"Redundant fact version {i}");
        _llmProvider.SetChatResult("Consolidated: a single fact");

        var result = await _service.ConsolidateAsync(_crewId, Ct);

        Assert.Equal(5, result.MergedCount);
        var search = Assert.Single(_application.Searches);
        Assert.Equal(nameof(IMemoryProvider.SearchSimilarAsync), search.Method);
        Assert.Equal(CrewMemoryScope.CrewMemoryKind, search.Filter?[CrewMemoryScope.KindProperty]);
        Assert.Equal(Crew, search.Filter?[CrewMemoryScope.CrewProperty]);
        Assert.Equal(5, _application.Deleted.Count);
        var merged = await _applicationStore.GetAsync(Assert.Single(result.CreatedMemoryIds), Ct);
        Assert.Equal("Consolidated: a single fact", merged!.Content);
        Assert.Equal(Crew, merged.Metadata.CustomProperties![CrewMemoryScope.CrewProperty]);
    }

    [Fact]
    public async Task ConsolidateAsync_with_few_memories_merges_nothing()
    {
        await SeedAsync(_crewId, "Item 1");
        await SeedAsync(_crewId, "Item 2");

        var result = await _service.ConsolidateAsync(_crewId, Ct);

        Assert.Equal(0, result.MergedCount);
        Assert.Equal(0, result.PrunedCount);
        Assert.Equal(2, result.UnchangedCount);
    }

    [Fact]
    public async Task AnalyzeAsync_DryRun_DoesNotStore()
    {
        _llmProvider.SetChatResult("""
            {"importance": 0.6, "category": "observation", "key_entities": [], "summary": "A note", "suggested_tags": ["note"], "reasoning": "dry run"}
            """);

        var analysis = await _service.AnalyzeAsync(_crewId, "Some content for dry run analysis", cancellationToken: Ct);

        Assert.Equal(0.6f, analysis.Importance, 0.01f);
        Assert.Equal("observation", analysis.Category);
        Assert.Empty(_application.Stored);
    }
}
