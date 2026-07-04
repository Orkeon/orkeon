using Orkeon.Application.Configuration;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.LLMs.Embeddings;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Embeddings;

public class CachedEmbeddingProviderTestsFixture
{
    private readonly MockEmbeddingProvider _mockInner;
    private readonly EmbeddingCacheOptions _cacheOptions;
    private readonly CachedEmbeddingProvider _provider;

    public CachedEmbeddingProviderTestsFixture()
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

    public CachedEmbeddingProviderTestsFixture WithMockInner(MockEmbeddingProvider value)
    {
        // Configure _mockInner as needed
        return this;
    }

    public CachedEmbeddingProviderTestsFixture WithCacheOptions(EmbeddingCacheOptions value)
    {
        // Configure _cacheOptions as needed
        return this;
    }

    public MockEmbeddingProvider GetMockInner() => _mockInner;
    public EmbeddingCacheOptions GetCacheOptions() => _cacheOptions;
    public CachedEmbeddingProvider GetProvider() => _provider;

}
