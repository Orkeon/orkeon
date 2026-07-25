using Orkeon.Domain.Memory;
using Orkeon.Infrastructure.Memory;

namespace Orkeon.Infrastructure.Tests.Memory;

/// <summary>
/// RAG-01/C5: <see cref="InMemoryProvider.SearchSimilarAsync"/> used to silently ignore its
/// <c>filter</c> parameter. These tests prove the metadata filter is honored with the same
/// semantics as the Redis/SQLite providers: <c>source</c> equality, <c>tag</c>/<c>tags</c>
/// membership, other keys matched against custom properties.
/// </summary>
public class InMemoryProviderMetadataFilterTests
{
    private static readonly float[] s_unitX = [1f, 0f, 0f];
    private static readonly float[] s_nearX = [0.9f, 0.1f, 0f];

    private readonly InMemoryProvider _provider = new();

    private async Task SeedAsync(CancellationToken ct)
    {
        await _provider.StoreWithEmbeddingAsync("k1",
            MemoryItem.Create(
                "from docs",
                source: "docs",
                tags: ["howto"],
                customProperties: new Dictionary<string, string> { ["category"] = "tech" }),
            s_unitX, ct);

        await _provider.StoreWithEmbeddingAsync("k2",
            MemoryItem.Create(
                "from wiki",
                source: "wiki",
                tags: ["reference"],
                customProperties: new Dictionary<string, string> { ["category"] = "science" }),
            s_nearX, ct);
    }

    [Fact]
    public async Task ShouldFilterBySource_WhenSearchSimilarAsync()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);

        // Act
        var results = await _provider.SearchSimilarAsync(
            s_unitX, topK: 10,
            filter: new Dictionary<string, object> { ["source"] = "wiki" },
            cancellationToken: ct);

        // Assert — only the wiki item, even though the docs item scores higher.
        Assert.Single(results);
        Assert.Equal("from wiki", results[0].Item.Content);
    }

    [Fact]
    public async Task ShouldFilterByTag_WhenSearchSimilarAsync()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);

        // Act
        var results = await _provider.SearchSimilarAsync(
            s_unitX, topK: 10,
            filter: new Dictionary<string, object> { ["tags"] = "howto" },
            cancellationToken: ct);

        // Assert
        Assert.Single(results);
        Assert.Equal("from docs", results[0].Item.Content);
    }

    [Fact]
    public async Task ShouldFilterByCustomProperty_WhenSearchSimilarAsync()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);

        // Act
        var results = await _provider.SearchSimilarAsync(
            s_unitX, topK: 10,
            filter: new Dictionary<string, object> { ["category"] = "science" },
            cancellationToken: ct);

        // Assert
        Assert.Single(results);
        Assert.Equal("from wiki", results[0].Item.Content);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenFilterMatchesNothing()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);

        // Act
        var results = await _provider.SearchSimilarAsync(
            s_unitX, topK: 10,
            filter: new Dictionary<string, object> { ["source"] = "does-not-exist" },
            cancellationToken: ct);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldRequireAllFilterEntries_WhenSearchSimilarAsync()
    {
        // Arrange — AND semantics: source matches but category does not.
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);

        // Act
        var results = await _provider.SearchSimilarAsync(
            s_unitX, topK: 10,
            filter: new Dictionary<string, object>
            {
                ["source"] = "docs",
                ["category"] = "science",
            },
            cancellationToken: ct);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldReturnAll_WhenFilterIsNullOrEmpty()
    {
        // Arrange
        var ct = TestContext.Current.CancellationToken;
        await SeedAsync(ct);

        // Act
        var withNull = await _provider.SearchSimilarAsync(
            s_unitX, topK: 10, filter: null, cancellationToken: ct);
        var withEmpty = await _provider.SearchSimilarAsync(
            s_unitX, topK: 10, filter: [], cancellationToken: ct);

        // Assert
        Assert.Equal(2, withNull.Count);
        Assert.Equal(2, withEmpty.Count);
    }
}
