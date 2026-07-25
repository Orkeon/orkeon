using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.LLMs.Embeddings;

/// <summary>
/// Adapts the Application-port <see cref="IEmbeddingProvider"/> to the
/// <see cref="IEmbeddingService"/> port consumed by agent selection and search tools.
/// </summary>
/// <remarks>
/// This is the default <see cref="IEmbeddingService"/> wiring since RAG-02/C5 (the
/// hash-based <c>SimpleEmbeddingService</c> is gone): the underlying provider is the
/// semantic-first resolution chain of <see cref="DefaultEmbeddingProviderResolver"/>
/// (local BGE via <c>AddOrkeonLocalEmbeddings()</c> → remote <c>Orkeon:Embeddings</c>
/// configuration → fail-fast at first use). Consumers therefore inherit real semantic
/// embeddings — or a loud, actionable error — never a silent lexical stub.
/// </remarks>
public sealed class EmbeddingProviderServiceAdapter : IEmbeddingService
{
    private readonly IEmbeddingProvider _provider;

    /// <summary>Initializes the adapter over the Application embedding port.</summary>
    public EmbeddingProviderServiceAdapter(IEmbeddingProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<float[]> GetEmbeddingAsync(string text)
        => _provider.GetEmbeddingAsync(text ?? string.Empty, CancellationToken.None);
}
