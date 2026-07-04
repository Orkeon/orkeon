using Orkeon.Analysis.Abstractions.Models;

namespace Orkeon.Analysis.Abstractions.Interfaces;

public interface IVectorStoreProvider
{
    Task IndexAsync(IReadOnlyList<VectorDocument> documents, CancellationToken ct);

    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        string queryText,
        ReadOnlyMemory<float> queryEmbedding,
        int topK,
        IReadOnlyDictionary<string, object>? filters,
        CancellationToken ct);

    Task DeleteAsync(IEnumerable<string> ids, CancellationToken ct);
}
