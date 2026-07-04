using Orkeon.Application.Configuration;
using static Orkeon.Tests.Shared.Constants.TestLlmConstants;

namespace Orkeon.Infrastructure.Tests.LLMs.Embeddings;

public class OllamaEmbeddingProviderTestsFixture
{
    private readonly EmbeddingOptions _options;

    public OllamaEmbeddingProviderTestsFixture()
    {
        _options = new EmbeddingOptions
        {
            Provider = ProviderOllama,
            Model = "nomic-embed-text",
            Dimension = 768
        };
    }

    public OllamaEmbeddingProviderTestsFixture WithOptions(EmbeddingOptions value)
    {
        // Configure _options as needed
        return this;
    }

    public EmbeddingOptions GetOptions() => _options;

}
