using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Tools.Search;

namespace Orkeon.Infrastructure.Tests.Tools.Search;

public sealed class SearchToolTestsFixture : IDisposable
{
    private static readonly float[] s_defaultEmbedding = [0.1f, 0.2f, 0.3f];

    private readonly MockEmbeddingService _mockEmbeddingService;
    private readonly MockVectorMemoryStore _mockVectorStore;
    private readonly SearchTool _tool;

    public SearchToolTestsFixture()
    {
        _mockEmbeddingService = new MockEmbeddingService();
        _mockVectorStore = new MockVectorMemoryStore();
        // Default setup: return a simple embedding
        _mockEmbeddingService.SetEmbeddingResult(s_defaultEmbedding);
        // Default setup: return empty results
        _mockVectorStore.SetSearchSimilarResult([]);
        _tool = new SearchTool(
        _mockEmbeddingService,
        _mockVectorStore
        );
    }

    public SearchToolTestsFixture WithMockEmbeddingService(MockEmbeddingService value)
    {
        // Configure _mockEmbeddingService as needed
        return this;
    }

    public SearchToolTestsFixture WithMockVectorStore(MockVectorMemoryStore value)
    {
        // Configure _mockVectorStore as needed
        return this;
    }

    public MockEmbeddingService GetMockEmbeddingService() => _mockEmbeddingService;
    public MockVectorMemoryStore GetMockVectorStore() => _mockVectorStore;
    public SearchTool GetTool() => _tool;


    public void Dispose()
    {
        _tool.Dispose();
        GC.SuppressFinalize(this);
    }
}
