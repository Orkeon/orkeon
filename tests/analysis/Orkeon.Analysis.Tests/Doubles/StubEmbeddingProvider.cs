using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Analysis.Tests.Doubles;

/// <summary>Embedder of a configurable size: each text gets a vector of <see cref="Size"/> equal values.</summary>
public sealed class StubEmbeddingProvider : IEmbeddingProvider
{
    public int Size = 2;

    public int Dimensions => Size;

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct)
    {
        var vectors = texts.Select(_ => new ReadOnlyMemory<float>(Enumerable.Repeat(0.1f, Size).ToArray())).ToList();
        return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(vectors);
    }
}
