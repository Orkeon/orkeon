using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Infrastructure.Memory.Cognitive;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Tests.Shared.Doubles;
using Microsoft.Extensions.Options;

namespace Orkeon.Infrastructure.Tests.Memory.Cognitive;

public class CognitiveMemoryServiceTests
{
    private static readonly float[] EmbeddingStandard = [0.1f, 0.2f, 0.3f];
    private static readonly float[] EmbeddingMerge = [0.5f, 0.5f, 0.5f];

    private readonly MockMemoryService _memoryService;
    private readonly MockMemoryProvider _memoryProvider;
    private readonly MockEmbeddingProvider _embeddingProvider;
    private readonly MockLlmProvider _llmProvider;
    private readonly CognitiveMemoryService _service;
    private readonly CognitiveMemoryOptions _cognitiveOptions;
    private readonly CrewId _crewId = CrewId.Create();

    public CognitiveMemoryServiceTests()
    {
        _memoryService = new MockMemoryService();
        _memoryProvider = new MockMemoryProvider();
        _embeddingProvider = new MockEmbeddingProvider();
        _llmProvider = new MockLlmProvider();
        _cognitiveOptions = new CognitiveMemoryOptions();
        var options = Options.Create(_cognitiveOptions);

        var analyzerLogger = new MockLogger<MemoryAnalyzer>();
        var detectorLogger = new MockLogger<ContradictionDetector>();
        var consolidatorLogger = new MockLogger<MemoryConsolidator>();
        var serviceLogger = new MockLogger<CognitiveMemoryService>();

        var analyzer = new MemoryAnalyzer(_llmProvider, options, analyzerLogger);
        var detector = new ContradictionDetector(_llmProvider, options, detectorLogger);
        var consolidator = new MemoryConsolidator(_llmProvider, _memoryProvider, options, consolidatorLogger);
        var scorer = new CompositeScorer(options);

        var analysisServices = new CognitiveAnalysisServices(analyzer, detector, consolidator, scorer);
        _service = new CognitiveMemoryService(
            _memoryService,
            _memoryProvider,
            _embeddingProvider,
            analysisServices,
            options,
            serviceLogger);
    }

    private void SetupDefaultLlmResponses()
    {
        // Default: analysis returns valid JSON, no contradictions
        var callCount = 0;
        _llmProvider.SetChatFunc((messages, config) =>
        {
            callCount++;
            // First call is analysis, second is contradiction check
            if (callCount == 1)
            {
                return new LlmResponse
                {
                    Content = """{"importance": 0.7, "category": "fact", "key_entities": ["test"], "summary": "Test summary", "suggested_tags": ["test"], "reasoning": "test"}"""
                };
            }
            return new LlmResponse
            {
                Content = """{"has_contradiction": false, "conflicting_ids": [], "description": "", "resolution": "", "action": "keep_both"}"""
            };
        });
    }

    [Fact]
    public async Task RememberAsync_FullPipeline_StoresEnrichedItem()
    {
        // Arrange
        SetupDefaultLlmResponses();
        _embeddingProvider.SetEmbeddingResult(EmbeddingStandard);

        // Act
        var result = await _service.RememberAsync(_crewId, "Test content to remember", "test context", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test content to remember", result.Content);
        Assert.Equal(0.7f, result.Importance, 0.01f);
        Assert.True(_embeddingProvider.GetEmbeddingCallCount >= 1);
        Assert.True(_memoryProvider.StoreWithEmbeddingCallCount >= 1);
        Assert.True(_memoryService.SaveCallCount >= 1);
    }

    [Fact]
    public async Task RememberAsync_ContradictionKeepNew_DeletesOld()
    {
        // Arrange
        var existingItem = MemoryItem.Create("Old content", importance: 0.5f);
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
                    Content = """{"importance": 0.8, "category": "fact", "key_entities": [], "summary": "Updated info", "suggested_tags": [], "reasoning": "test"}"""
                };
            }
            return new LlmResponse
            {
                Content = $$"""{"has_contradiction": true, "conflicting_ids": ["{{existingItem.Id}}"], "description": "Conflict", "resolution": "Use new", "action": "keep_new"}"""
            };
        });
        _embeddingProvider.SetEmbeddingResult(EmbeddingStandard);

        // Act
        var result = await _service.RememberAsync(_crewId, "New content replacing old", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.True(_memoryProvider.DeleteCallCount >= 1);
    }

    [Fact]
    public async Task RememberAsync_ContradictionKeepExisting_ReturnsExisting()
    {
        // Arrange
        var existingItem = MemoryItem.Create("Existing content", importance: 0.9f);
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
                    Content = """{"importance": 0.3, "category": "fact", "key_entities": [], "summary": "Less important", "suggested_tags": [], "reasoning": "test"}"""
                };
            }
            return new LlmResponse
            {
                Content = $$"""{"has_contradiction": true, "conflicting_ids": ["{{existingItem.Id}}"], "description": "Conflict", "resolution": "Keep existing", "action": "keep_existing"}"""
            };
        });

        // Act
        var result = await _service.RememberAsync(_crewId, "New less important content", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Existing content", result.Content);
    }

    [Fact]
    public async Task RememberAsync_ContradictionMerge_StoresMerged()
    {
        // Arrange
        var existingItem = MemoryItem.Create("Part A of the story", importance: 0.6f);
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
                    Content = """{"importance": 0.7, "category": "fact", "key_entities": [], "summary": "Story part", "suggested_tags": ["story"], "reasoning": "test"}"""
                };
            }
            return new LlmResponse
            {
                Content = $$"""{"has_contradiction": true, "conflicting_ids": ["{{existingItem.Id}}"], "description": "Overlap", "resolution": "Merge both", "action": "merge"}"""
            };
        });
        _embeddingProvider.SetEmbeddingResult(EmbeddingMerge);

        // Act
        var result = await _service.RememberAsync(_crewId, "Part B of the story", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("Part B of the story", result.Content);
        Assert.Contains("Part A of the story", result.Content);
    }

    [Fact]
    public async Task RememberAsync_ContradictionKeepBoth_StoresBoth()
    {
        // Arrange
        var existingItem = MemoryItem.Create("Existing fact", importance: 0.5f);
        _memoryService.SetSearchResult([existingItem]);

        var callCount = 0;
        _llmProvider.SetChatFunc((messages, config) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new LlmResponse
                {
                    Content = """{"importance": 0.5, "category": "fact", "key_entities": [], "summary": "Another fact", "suggested_tags": [], "reasoning": "test"}"""
                };
            }
            return new LlmResponse
            {
                Content = """{"has_contradiction": false, "conflicting_ids": [], "description": "", "resolution": "", "action": "keep_both"}"""
            };
        });
        _embeddingProvider.SetEmbeddingResult(EmbeddingStandard);

        // Act
        var result = await _service.RememberAsync(_crewId, "New complementary fact", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("New complementary fact", result.Content);
        Assert.Equal(0, _memoryProvider.DeleteCallCount); // Nothing deleted
    }

    [Fact]
    public async Task RecallAsync_ReturnsCompositeScored()
    {
        // Arrange
        var item1 = MemoryItem.Create("Relevant content", importance: 0.8f);
        var item2 = MemoryItem.Create("Somewhat relevant", importance: 0.4f);
        _memoryProvider.SetSearchSimilarResult(
        [
            new(item1, 0.9f),
            new(item2, 0.6f)
        ]);
        _embeddingProvider.SetEmbeddingResult(EmbeddingStandard);

        // Act
        var results = await _service.RecallAsync(_crewId, "search query", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.True(results[0].CompositeScore >= results[1].CompositeScore);
        Assert.True(_embeddingProvider.GetEmbeddingCallCount >= 1);
    }

    [Fact]
    public async Task ConsolidateAsync_DelegatesToConsolidator()
    {
        // Arrange - few items so consolidation skips
        var items = new List<MemoryItem>
        {
            MemoryItem.Create("Item 1"),
            MemoryItem.Create("Item 2")
        };
        _memoryService.SetSearchResult(items);

        // Act
        var result = await _service.ConsolidateAsync(_crewId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, result.MergedCount);
        Assert.Equal(0, result.PrunedCount);
        Assert.Equal(2, result.UnchangedCount);
    }

    [Fact]
    public async Task AnalyzeAsync_DryRun_DoesNotStore()
    {
        // Arrange
        _llmProvider.SetChatResult("""
            {"importance": 0.6, "category": "observation", "key_entities": [], "summary": "A note", "suggested_tags": ["note"], "reasoning": "dry run"}
            """);

        // Act
        var analysis = await _service.AnalyzeAsync(_crewId, "Some content for dry run analysis", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(analysis);
        Assert.Equal(0.6f, analysis.Importance, 0.01f);
        Assert.Equal("observation", analysis.Category);
        Assert.Equal(0, _memoryProvider.StoreCallCount); // Nothing stored
        Assert.Equal(0, _memoryService.SaveCallCount);   // Nothing saved
    }
}
