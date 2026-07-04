using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Domain.Common;
using Orkeon.Domain.SharedKernel;
using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.DependencyInjection;
using Orkeon.Infrastructure.Memory.Cognitive;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Tests.Memory.Cognitive;

public class CognitiveMemoryIntegrationTests
{
    private static readonly float[] EmbeddingContradiction = [0.3f, 0.6f, 0.1f];
    private static readonly float[] EmbeddingRecall = [0.1f, 0.2f, 0.3f];

    private readonly MockMemoryService _memoryService;
    private readonly MockMemoryProvider _memoryProvider;
    private readonly MockEmbeddingProvider _embeddingProvider;
    private readonly MockLlmProvider _llmProvider;
    private readonly CognitiveMemoryService _service;
    private readonly CrewId _crewId = CrewId.Create();

    public CognitiveMemoryIntegrationTests()
    {
        _memoryService = new MockMemoryService();
        _memoryProvider = new MockMemoryProvider();
        _embeddingProvider = new MockEmbeddingProvider();
        _llmProvider = new MockLlmProvider();
        var options = Options.Create(new CognitiveMemoryOptions());

        var analyzer = new MemoryAnalyzer(_llmProvider, options, new MockLogger<MemoryAnalyzer>());
        var detector = new ContradictionDetector(_llmProvider, options, new MockLogger<ContradictionDetector>());
        var consolidator = new MemoryConsolidator(_llmProvider, _memoryProvider, options, new MockLogger<MemoryConsolidator>());
        var scorer = new CompositeScorer(options);

        var analysisServices = new CognitiveAnalysisServices(analyzer, detector, consolidator, scorer);
        _service = new CognitiveMemoryService(
            _memoryService, _memoryProvider, _embeddingProvider,
            analysisServices,
            options, new MockLogger<CognitiveMemoryService>());
    }

    [Fact]
    public async Task Remember_ThenRecall_ReturnsWithHighScore()
    {
        // Arrange
        var embedding = new float[] { 0.5f, 0.5f, 0.5f };
        _embeddingProvider.SetEmbeddingResult(embedding);

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
        var remembered = await _service.RememberAsync(_crewId, "Orkeon is an AI agent framework", cancellationToken: TestContext.Current.CancellationToken);

        // Setup for recall - return the stored item as a similar result
        _memoryProvider.SetSearchSimilarResult(
        [
            new(remembered, 0.95f)
        ]);

        // Act - Recall
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
        var existingItem = MemoryItem.Create("The deadline is Friday", importance: 0.7f);
        await _memoryProvider.StoreAsync(existingItem.Id, existingItem, TestContext.Current.CancellationToken);
        _memoryService.SetSearchResult([existingItem]);

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
        _embeddingProvider.SetEmbeddingResult(EmbeddingContradiction);

        // Act
        var result = await _service.RememberAsync(_crewId, "The deadline is Monday", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("The deadline is Monday", result.Content);
        Assert.True(_memoryProvider.DeleteCallCount >= 1); // Old memory deleted
    }

    [Fact]
    public async Task Consolidate_MergesRedundantMemories()
    {
        // Arrange - create redundant memories
        var embedding = new float[] { 1.0f, 0.0f, 0.0f };
        var items = new List<MemoryItem>();
        for (var i = 0; i < 5; i++)
        {
            var item = MemoryItem.Create($"Redundant fact version {i}", embedding: embedding, importance: 0.5f);
            items.Add(item);
            await _memoryProvider.StoreAsync(item.Id, item, TestContext.Current.CancellationToken);
        }
        _memoryService.SetSearchResult(items);

        _llmProvider.SetChatResult("Consolidated: single fact combining all versions");

        // Act
        var result = await _service.ConsolidateAsync(_crewId, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.MergedCount > 0 || result.UnchangedCount > 0);
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
        services.AddSingleton<IMemoryProvider>(_memoryProvider);
        services.AddSingleton<IEmbeddingProvider>(_embeddingProvider);
        services.AddSingleton<IMemoryService>(_memoryService);
        services.AddLogging();

        // Register cognitive memory
        services.AddOrkeonCognitiveMemory(configuration);

        // Act
        var provider = services.BuildServiceProvider();
        var cognitiveService = provider.GetService<ICognitiveMemoryService>();

        // Assert
        Assert.NotNull(cognitiveService);
        Assert.IsType<CognitiveMemoryService>(cognitiveService);
    }

    [Fact]
    public async Task RecallWithOptions_RespectsWeights()
    {
        // Arrange
        var highImportanceItem = MemoryItem.Create("Very important fact", importance: 1.0f);
        var highSimilarityItem = MemoryItem.Create("Closely related fact", importance: 0.1f);

        _memoryProvider.SetSearchSimilarResult(
        [
            new(highImportanceItem, 0.3f),   // low similarity, high importance
            new(highSimilarityItem, 0.95f)   // high similarity, low importance
        ]);
        _embeddingProvider.SetEmbeddingResult(EmbeddingRecall);

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
        Assert.Equal(1.0f, importanceWeighted[0].ImportanceScore);

        // With similarity weight, high similarity item should be first
        Assert.Equal(0.95f, similarityWeighted[0].SemanticScore);
    }
}
