using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Memory;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Memory;
using Orkeon.Infrastructure.Memory.Cognitive;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Tests.Memory.Cognitive;

/// <summary>
/// The cognitive memory end to end, over the real memory of a named crew — the host's default
/// provider, In-Memory here, scoped by the crew's name (GAP-30).
/// </summary>
public sealed class CognitiveMemoryIntegrationTests : IDisposable
{
    private static readonly float[] EmbeddingContradiction = [0.3f, 0.6f, 0.1f];

    private readonly InMemoryProvider _store = new();
    private readonly CrewMemoryProviderRegistry _registry = new();
    private readonly MemoryService _memoryService;
    private readonly MockEmbeddingProvider _embeddingProvider;
    private readonly MockLlmProvider _llmProvider;
    private readonly CognitiveMemoryService _service;
    private readonly CrewId _crewId = CrewId.Create();

    public CognitiveMemoryIntegrationTests()
    {
        _memoryService = new MemoryService(new StubMemoryProviderFactory(_store), NullLogger<MemoryService>.Instance, _registry, _store);
        _registry.Record(_crewId, providerType: null, "news-desk", memoryEnabled: true);
        _embeddingProvider = new MockEmbeddingProvider();
        _llmProvider = new MockLlmProvider();
        var options = Options.Create(new CognitiveMemoryOptions());

        var analyzer = new MemoryAnalyzer(_llmProvider, options, new MockLogger<MemoryAnalyzer>());
        var detector = new ContradictionDetector(_llmProvider, options, new MockLogger<ContradictionDetector>());
        var consolidator = new MemoryConsolidator(_llmProvider, _embeddingProvider, options, new MockLogger<MemoryConsolidator>());
        var scorer = new CompositeScorer(options);

        var analysisServices = new CognitiveAnalysisServices(analyzer, detector, consolidator, scorer);
        _service = new CognitiveMemoryService(
            _memoryService, _embeddingProvider,
            analysisServices,
            options, new MockLogger<CognitiveMemoryService>());
    }

    public void Dispose() => _memoryService.Dispose();

    private ILongTermMemory CrewMemory => _memoryService.GetMemorySystem(_crewId).LongTerm;

    [Fact]
    public async Task Remember_ThenRecall_ReturnsWithHighScore()
    {
        // Arrange
        _embeddingProvider.SetEmbeddingResult([0.5f, 0.5f, 0.5f]);

        // Analysis response
        var callCount = 0;
        _llmProvider.SetChatFunc((messages, config) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new LlmResponse
                {
                    Content = """{"importance": 0.9, "category": "fact", "key_entities": ["Orkeon"], "summary": "Framework info", "suggested_tags": ["ai"], "reasoning": "important"}"""
                };
            }
            return new LlmResponse
            {
                Content = """{"has_contradiction": false, "conflicting_ids": [], "description": "", "resolution": "", "action": "keep_both"}"""
            };
        });

        // Remember
        await _service.RememberAsync(_crewId, "Orkeon is an AI agent framework", cancellationToken: TestContext.Current.CancellationToken);

        // Act - Recall, by vector, in the crew's memory
        var results = await _service.RecallAsync(_crewId, "AI agent framework", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(results);
        Assert.True(results[0].CompositeScore > 0.5f);
        Assert.Equal("Orkeon is an AI agent framework", results[0].Item.Content);
    }

    [Fact]
    public async Task Remember_Contradiction_ResolvesCorrectly()
    {
        // Arrange
        _embeddingProvider.SetEmbeddingResult(EmbeddingContradiction);
        var existingItem = MemoryItem.Create("The deadline is Friday", embedding: EmbeddingContradiction, importance: 0.7f);
        await CrewMemory.AddAsync(existingItem);

        var callCount = 0;
        _llmProvider.SetChatFunc((messages, config) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new LlmResponse
                {
                    Content = """{"importance": 0.8, "category": "decision", "key_entities": ["deadline"], "summary": "Deadline change", "suggested_tags": ["schedule"], "reasoning": "updated"}"""
                };
            }
            return new LlmResponse
            {
                Content = $$"""{"has_contradiction": true, "conflicting_ids": ["{{existingItem.Id}}"], "description": "Deadline changed", "resolution": "Use new deadline", "action": "keep_new"}"""
            };
        });

        // Act
        var result = await _service.RememberAsync(_crewId, "The deadline is Monday", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("The deadline is Monday", result.Content);
        Assert.Null(await _store.GetAsync(existingItem.Id.ToString(), TestContext.Current.CancellationToken)); // Old memory deleted
    }

    [Fact]
    public async Task Consolidate_MergesRedundantMemories()
    {
        // Arrange - create redundant memories
        _embeddingProvider.SetEmbeddingResult([1.0f, 0.0f, 0.0f]);
        for (var i = 0; i < 5; i++)
            await CrewMemory.AddAsync(MemoryItem.Create($"Redundant fact version {i}", embedding: [1.0f, 0.0f, 0.0f], importance: 0.5f));

        _llmProvider.SetChatResult("Consolidated: single fact combining all versions");

        // Act
        var result = await _service.ConsolidateAsync(_crewId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(5, result.MergedCount);
        var remaining = await CrewMemory.SearchSimilarAsync([1.0f, 0.0f, 0.0f], 10, -1f, TestContext.Current.CancellationToken);
        Assert.Equal("Consolidated: single fact combining all versions", Assert.Single(remaining).Item.Content);
    }

    [Fact]
    public void DI_Registration_ResolvesService()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orkeon:CognitiveMemory:EnableLlmAnalysis"] = "true",
                ["Orkeon:CognitiveMemory:AnalysisTemperature"] = "0.1"
            })
            .Build();

        // Register dependencies
        services.AddSingleton<ILlmProvider>(_llmProvider);
        services.AddSingleton<IEmbeddingProvider>(_embeddingProvider);
        services.AddSingleton<IMemoryService>(_memoryService);
        services.AddLogging();

        // Register cognitive memory
        services.AddOrkeonCognitiveMemory(configuration);

        // Act
        using var provider = services.BuildServiceProvider();
        var cognitiveService = provider.GetService<ICognitiveMemoryService>();

        // Assert
        Assert.NotNull(cognitiveService);
        Assert.IsType<CognitiveMemoryService>(cognitiveService);
    }

    [Fact]
    public async Task RecallWithOptions_RespectsWeights()
    {
        // Arrange — the query is close to one memory, far from the other
        _embeddingProvider.SetEmbeddingResult([1f, 0f]);
        await CrewMemory.AddAsync(MemoryItem.Create("Very important fact", embedding: [0.3f, 0.954f], importance: 1.0f));
        await CrewMemory.AddAsync(MemoryItem.Create("Closely related fact", embedding: [0.95f, 0.312f], importance: 0.1f));

        // Act - weight importance heavily
        var importanceWeighted = await _service.RecallAsync(_crewId, "test query", new RecallOptions
        {
            SemanticWeight = 0.1f,
            RecencyWeight = 0.1f,
            ImportanceWeight = 0.8f,
            TopK = 10,
            MinScore = 0.0f
        }, TestContext.Current.CancellationToken);

        // Act - weight similarity heavily
        var similarityWeighted = await _service.RecallAsync(_crewId, "test query", new RecallOptions
        {
            SemanticWeight = 0.8f,
            RecencyWeight = 0.1f,
            ImportanceWeight = 0.1f,
            TopK = 10,
            MinScore = 0.0f
        }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, importanceWeighted.Count);
        Assert.Equal(2, similarityWeighted.Count);

        // With importance weight, high importance item should be first
        Assert.Equal("Very important fact", importanceWeighted[0].Item.Content);

        // With similarity weight, high similarity item should be first
        Assert.Equal("Closely related fact", similarityWeighted[0].Item.Content);
    }
}
