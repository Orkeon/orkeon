using Orkeon.Application.Rag;
using Orkeon.Application.Tests.Doubles;
using static Orkeon.Tests.Shared.Constants.TestToolParamKeys;

namespace Orkeon.Application.Tests.Rag;

public class KnowledgeRetrieverTests
{
    private readonly MockKnowledgeService _mockKnowledgeService = new();
    private readonly MockEmbeddingProvider _mockEmbeddingProvider = new();

    private static List<KnowledgeItem> SampleKnowledgeItems() =>
    [
        new KnowledgeItem("1", "First document about AI", "docs",
            [], null, 0.9, DateTime.UtcNow),
        new KnowledgeItem("2", "Second document about ML", "docs",
            [], null, 0.8, DateTime.UtcNow),
        new KnowledgeItem("3", "Third document about NLP", "papers",
            [], null, 0.6, DateTime.UtcNow),
    ];

    private void SetupKnowledgeService(List<KnowledgeItem>? items = null)
    {
        items ??= SampleKnowledgeItems();
        _mockKnowledgeService.SetSearchResult(items.AsReadOnly());
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnChunksSortedByScore_WhenRetrievingAsync()
    {
        SetupKnowledgeService();
        var retriever = new KnowledgeRetriever(_mockKnowledgeService);

        var result = await retriever.RetrieveAsync(
            ParamQuery, new RetrievalOptions { TopK = 10, MinRelevanceScore = 0.0f }, TestContext.Current.CancellationToken);

        Assert.True(result.Count > 0);
        for (int i = 1; i < result.Count; i++)
        {
            Assert.True(result[i - 1].RelevanceScore >= result[i].RelevanceScore,
                "Chunks should be sorted by score descending");
        }
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldFilterMinRelevanceScore_WhenRetrievingAsync()
    {
        SetupKnowledgeService();
        var retriever = new KnowledgeRetriever(_mockKnowledgeService);

        var result = await retriever.RetrieveAsync(
            ParamQuery, new RetrievalOptions { TopK = 10, MinRelevanceScore = 0.75f }, TestContext.Current.CancellationToken);

        Assert.All(result, chunk =>
            Assert.True(chunk.RelevanceScore >= 0.75f,
                $"Chunk score {chunk.RelevanceScore} should be >= 0.75"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldFilterSourceFilter_WhenRetrievingAsync()
    {
        // Source filtering is done via IKnowledgeService.SearchAsync sources parameter
        SetupKnowledgeService();
        var retriever = new KnowledgeRetriever(_mockKnowledgeService);

        var options = new RetrievalOptions
        {
            TopK = 10,
            MinRelevanceScore = 0.0f,
            SourceFilter = ["docs"]
        };

        await retriever.RetrieveAsync(ParamQuery, options, TestContext.Current.CancellationToken);

        Assert.Equal(1, _mockKnowledgeService.SearchCallCount);
        Assert.Equal(ParamQuery, _mockKnowledgeService.LastSearchQuery);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDeduplicatesContent_WhenRetrievingAsync()
    {
        var items = new List<KnowledgeItem>
        {
            new("1", "Duplicate content here", "src1", null, null, 0.9, DateTime.UtcNow),
            new("2", "Duplicate content here", "src2", null, null, 0.85, DateTime.UtcNow),
            new("3", "Unique content", "src1", null, null, 0.8, DateTime.UtcNow),
        };

        SetupKnowledgeService(items);
        var retriever = new KnowledgeRetriever(_mockKnowledgeService);

        var result = await retriever.RetrieveAsync(
            ParamQuery, new RetrievalOptions { TopK = 10, MinRelevanceScore = 0.0f }, TestContext.Current.CancellationToken);

        // Duplicate content should be deduplicated
        var contents = result.Select(r => r.Content).ToList();
        Assert.Equal(contents.Distinct().Count(), contents.Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldUseEmbeddingProviderWhenAvailable_WhenRetrievingAsync()
    {
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        var itemEmbedding = new float[] { 0.1f, 0.2f, 0.3f }; // Same embedding = perfect match

        var items = new List<KnowledgeItem>
        {
            new("1", "Content with embedding", "src",
                [], itemEmbedding, 0.5, DateTime.UtcNow),
        };

        SetupKnowledgeService(items);
        _mockEmbeddingProvider.SetEmbeddingResult(embedding);

        var retriever = new KnowledgeRetriever(_mockKnowledgeService, _mockEmbeddingProvider);

        var result = await retriever.RetrieveAsync(
            ParamQuery, new RetrievalOptions { TopK = 5, MinRelevanceScore = 0.0f }, TestContext.Current.CancellationToken);

        Assert.Single(result);
        // With identical embeddings, cosine similarity should be ~1.0
        Assert.True(result[0].RelevanceScore > 0.99f,
            $"Expected cosine similarity near 1.0, got {result[0].RelevanceScore}");

        Assert.Equal(1, _mockEmbeddingProvider.GetEmbeddingCallCount);
        Assert.Equal(ParamQuery, _mockEmbeddingProvider.LastGetEmbeddingText);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldFallsBackToSimilarityScoreWithoutEmbeddings_WhenRetrievingAsync()
    {
        var items = new List<KnowledgeItem>
        {
            new("1", "Content", "src", null, null, 0.75, DateTime.UtcNow),
        };

        SetupKnowledgeService(items);
        // No embedding provider
        var retriever = new KnowledgeRetriever(_mockKnowledgeService);

        var result = await retriever.RetrieveAsync(
            ParamQuery, new RetrievalOptions { TopK = 5, MinRelevanceScore = 0.0f }, TestContext.Current.CancellationToken);

        Assert.Single(result);
        Assert.Equal(0.75f, result[0].RelevanceScore, 0.01f);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRespectsTopK_WhenRetrievingAsync()
    {
        var items = new List<KnowledgeItem>();
        for (int i = 0; i < 10; i++)
        {
            items.Add(new KnowledgeItem(
                $"{i}", $"Content {i}", "src", null, null, 0.9 - i * 0.05, DateTime.UtcNow));
        }

        SetupKnowledgeService(items);
        var retriever = new KnowledgeRetriever(_mockKnowledgeService);

        var result = await retriever.RetrieveAsync(
            ParamQuery, new RetrievalOptions { TopK = 3, MinRelevanceScore = 0.0f }, TestContext.Current.CancellationToken);

        Assert.True(result.Count <= 3, $"Expected at most 3 results, got {result.Count}");
    }
}
