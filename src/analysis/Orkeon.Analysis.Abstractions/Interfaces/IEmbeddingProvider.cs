using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.Interfaces;

/// <summary>
/// Computes vector embeddings for RaggableTree node texts (embed phase of the pipeline).
/// </summary>
public interface IEmbeddingProvider
{
    int Dimensions { get; }

    /// <summary>
    /// The name of the model that computes the embeddings (<c>bge-micro-v2</c>, <c>text-embedding-3-small</c>…),
    /// recorded with an index and its cache; <see langword="null"/> when the provider does not name it.
    /// </summary>
    string? ModelName => null;

    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct);

    Task EmbedAsync(
        IEnumerable<RaggableNode> nodes,
        string model,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return EmbedCoreAsync(nodes, ct);

        async Task EmbedCoreAsync(IEnumerable<RaggableNode> source, CancellationToken token)
        {
            var targets = source.Where(n => !string.IsNullOrEmpty(n.EmbeddingText)).ToList();
            if (targets.Count == 0) return;
            var texts = targets.Select(n => n.EmbeddingText).ToList();
            var vectors = await EmbedBatchAsync(texts, token).ConfigureAwait(false);
            for (var i = 0; i < targets.Count && i < vectors.Count; i++)
            {
                targets[i].Embedding = vectors[i];
            }
        }
    }
}
