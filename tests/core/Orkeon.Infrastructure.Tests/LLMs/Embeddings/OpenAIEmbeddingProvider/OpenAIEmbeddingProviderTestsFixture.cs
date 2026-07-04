using Orkeon.Application.Configuration;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.LLMs.Embeddings;
using Orkeon.Infrastructure.Tests.Doubles;

namespace Orkeon.Infrastructure.Tests.LLMs.Embeddings;

public sealed class OpenAIEmbeddingProviderTestsFixture : IDisposable
{
    private readonly MockEmbeddingGenerator _mockGenerator;
    private readonly EmbeddingOptions _options;
    private readonly OpenAIEmbeddingProvider _provider;

    public OpenAIEmbeddingProviderTestsFixture()
    {
        _mockGenerator = new MockEmbeddingGenerator();
        _options = new EmbeddingOptions
        {
            Model = "text-embedding-3-small",
            Dimension = 1536
        };
        _provider = new OpenAIEmbeddingProvider(
        _mockGenerator,
        Options.Create(_options));
    }

    public OpenAIEmbeddingProviderTestsFixture WithMockGenerator(MockEmbeddingGenerator value)
    {
        // Configure _mockGenerator as needed
        return this;
    }

    public OpenAIEmbeddingProviderTestsFixture WithOptions(EmbeddingOptions value)
    {
        // Configure _options as needed
        return this;
    }

    public MockEmbeddingGenerator GetMockGenerator() => _mockGenerator;
    public EmbeddingOptions GetOptions() => _options;
    public OpenAIEmbeddingProvider GetProvider() => _provider;


    public void Dispose()
    {
        _mockGenerator.Dispose();
        GC.SuppressFinalize(this);
    }
}
