using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Hand-written double for the Analysis-side
/// <see cref="Orkeon.Analysis.Abstractions.Interfaces.IEmbeddingProvider"/>.
/// Records every <c>EmbedBatchAsync</c> call (texts + cancellation token) and returns
/// deterministic vectors of <see cref="Dimensions"/> floats (value = 1-based index of
/// the text within the batch), unless <see cref="VectorsToReturn"/> is set.
/// </summary>
public sealed class FakeAnalysisEmbeddingProvider : IEmbeddingProvider
{
    /// <summary>Dimension reported and used for generated vectors. Defaults to 384 (BGE-micro-v2).</summary>
    public int Dimensions { get; set; } = 384;

    /// <summary>Every batch received, in call order.</summary>
    public List<IReadOnlyList<string>> ReceivedBatches { get; } = [];

    /// <summary>Every cancellation token received, in call order.</summary>
    public List<CancellationToken> ReceivedTokens { get; } = [];

    /// <summary>Number of <c>EmbedBatchAsync</c> calls.</summary>
    public int CallCount => ReceivedBatches.Count;

    /// <summary>When set, returned verbatim instead of the generated vectors.</summary>
    public IReadOnlyList<ReadOnlyMemory<float>>? VectorsToReturn { get; set; }

    /// <summary>When true, honors the token by throwing if cancellation is requested.</summary>
    public bool ThrowIfCancelled { get; set; }

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct)
    {
        ReceivedBatches.Add(texts);
        ReceivedTokens.Add(ct);

        if (ThrowIfCancelled)
        {
            ct.ThrowIfCancellationRequested();
        }

        if (VectorsToReturn is not null)
        {
            return Task.FromResult(VectorsToReturn);
        }

        var vectors = new ReadOnlyMemory<float>[texts.Count];
        for (var i = 0; i < texts.Count; i++)
        {
            var values = new float[Dimensions];
            Array.Fill(values, i + 1f);
            vectors[i] = values;
        }

        return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(vectors);
    }
}
