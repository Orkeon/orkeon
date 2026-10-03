using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Tools.Rag.Tests.Doubles;

/// <summary>
/// Hand-written <see cref="IEmbeddingProvider"/>: a constant vector of <see cref="Dimensions"/>
/// floats for every text, so an ingestion can run end to end without a model.
/// </summary>
public sealed class StubEmbeddingProvider : IEmbeddingProvider
{
    /// <inheritdoc />
    public string Name => "stub-embeddings";

    /// <inheritdoc />
    public string Model => "stub-embedding-model";

    /// <inheritdoc />
    public int Dimensions => 4;

    /// <inheritdoc />
    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(Vector());

    /// <inheritdoc />
    public Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        IList<float[]> vectors = [.. texts.Select(_ => Vector())];
        return Task.FromResult(vectors);
    }

    private float[] Vector() => Enumerable.Repeat(0.5f, Dimensions).ToArray();
}
