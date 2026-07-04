using Orkeon.Application.Configuration;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.LLMs.Embeddings;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Embeddings;

public class CachedEmbeddingProviderTests
{
    private readonly MockEmbeddingProvider _mockInner;
    private readonly EmbeddingCacheOptions _cacheOptions;
    private readonly CachedEmbeddingProvider _provider;

    public CachedEmbeddingProviderTests()
    {
        _mockInner = new MockEmbeddingProvider();
        _mockInner.Name = "TestProvider";
        _mockInner.Model = TestModelName;
        _mockInner.Dimensions = 384;

        _cacheOptions = new EmbeddingCacheOptions
        {
            SlidingExpirationMinutes = 60,
            MaxCacheSizeBytes = 100 * 1024 * 1024
        };

        _provider = new CachedEmbeddingProvider(
            _mockInner,
            Options.Create(_cacheOptions));
    }

    [Fact]
    public async Task ShouldReturnWithoutCallingInner_WhenGetEmbeddingAsyncCacheHit()
    {
        // Arrange
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        _mockInner.SetEmbeddingResult(embedding);

        // Act - first call populates cache
        await _provider.GetEmbeddingAsync("test text", TestContext.Current.CancellationToken);
        // Second call should hit cache
        var result = await _provider.GetEmbeddingAsync("test text", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(embedding, result);
        Assert.Equal(1, _mockInner.GetEmbeddingCallCount); // Only called once, second was cached
    }

    [Fact]
    public async Task ShouldCallInnerAndCaches_WhenGetEmbeddingAsyncCacheMiss()
    {
        // Arrange
        var embedding = new float[] { 0.1f, 0.2f, 0.3f };
        _mockInner.SetEmbeddingResult(embedding);

        // Act
        var result = await _provider.GetEmbeddingAsync("new text", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(embedding, result);
        Assert.Equal(1, _mockInner.GetEmbeddingCallCount);
    }

    [Fact]
    public async Task ShouldPartialCacheHitOnlyCallsInnerForMisses_WhenGetEmbeddingsAsync()
    {
        // Arrange
        var embedding1 = new float[] { 0.1f, 0.2f };
        var embedding2 = new float[] { 0.3f, 0.4f };
        var embedding3 = new float[] { 0.5f, 0.6f };

        // Pre-populate cache with text1
        _mockInner.SetEmbeddingResult(embedding1);
        await _provider.GetEmbeddingAsync("text1", TestContext.Current.CancellationToken);

        // Setup for batch call that should only request text2 and text3
        _mockInner.SetEmbeddingsResult([embedding2, embedding3]);

        // Act
        var results = await _provider.GetEmbeddingsAsync(
            ["text1", "text2", "text3"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, results.Count);
        Assert.Equal(embedding1, results[0]); // From cache
        Assert.Equal(embedding2, results[1]); // From inner
        Assert.Equal(embedding3, results[2]); // From inner
    }

    [Fact]
    public void ShouldDelegateToInner_WhenName()
    {
        Assert.Equal("TestProvider", _provider.Name);
    }

    [Fact]
    public void ShouldDelegateToInner_WhenModel()
    {
        Assert.Equal(TestModelName, _provider.Model);
    }

    [Fact]
    public void ShouldDelegateToInner_WhenDimensions()
    {
        Assert.Equal(384, _provider.Dimensions);
    }

    [Fact]
    public async Task ShouldCallInnerForEach_WhenGetEmbeddingAsyncDifferentTexts()
    {
        // Arrange
        _mockInner.SetEmbeddingFunc(text => [text.Length * 0.1f]);

        // Act
        await _provider.GetEmbeddingAsync("short", TestContext.Current.CancellationToken);
        await _provider.GetEmbeddingAsync("a longer text", TestContext.Current.CancellationToken);

        // Assert - both are cache misses, so inner called twice
        Assert.Equal(2, _mockInner.GetEmbeddingCallCount);
    }
}
