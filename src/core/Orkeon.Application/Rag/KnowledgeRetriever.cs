using Orkeon.Application.Interfaces.Ports;
using Orkeon.Application.Interfaces.Rag;

namespace Orkeon.Application.Rag;

/// <summary>
/// Retriever implementation that searches through IKnowledgeService and optionally
/// uses IEmbeddingProvider for computing cosine similarity scores.
/// </summary>
public sealed class KnowledgeRetriever : IRetriever
{
    private readonly IKnowledgeService _knowledgeService;
    private readonly IEmbeddingProvider? _embeddingProvider;

    /// <summary>
    /// Initializes a new instance of <see cref="KnowledgeRetriever"/>.
    /// </summary>
    public KnowledgeRetriever(
        IKnowledgeService knowledgeService,
        IEmbeddingProvider? embeddingProvider = null)
    {
        ArgumentNullException.ThrowIfNull(knowledgeService);
        _knowledgeService = knowledgeService;
        _embeddingProvider = embeddingProvider;
    }

    /// <inheritdoc />
    public System.Threading.Tasks.Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(
        string query, RetrievalOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return RetrieveCoreAsync();

        async System.Threading.Tasks.Task<IReadOnlyList<RetrievedChunk>> RetrieveCoreAsync()
        {
        // Retrieve more than TopK from knowledge service to allow for filtering
        var knowledgeResults = await _knowledgeService.SearchAsync(
            query,
            topK: options.TopK * 2,
            minSimilarity: 0.1,
            sources: options.SourceFilter?.ToArray(),
            cancellationToken: ct).ConfigureAwait(false);

        float[]? queryEmbedding = null;
        if (_embeddingProvider != null)
        {
            queryEmbedding = await _embeddingProvider.GetEmbeddingAsync(query, ct).ConfigureAwait(false);
        }

        var scored = new List<RetrievedChunk>();

        foreach (var item in knowledgeResults)
        {
            float score;
            if (queryEmbedding != null && item.Embedding != null)
            {
                score = CosineSimilarity(queryEmbedding, item.Embedding);
            }
            else
            {
                score = (float)(item.SimilarityScore ?? 0.5);
            }

            scored.Add(new RetrievedChunk
            {
                Content = item.Content,
                SourceId = item.Source,
                RelevanceScore = score,
                Metadata = item.Metadata ?? []
            });
        }

        // Deduplicate by content prefix
        var seen = new HashSet<string>();
        var result = new List<RetrievedChunk>();

        foreach (var chunk in scored.OrderByDescending(c => c.RelevanceScore))
        {
            var contentKey = chunk.Content.Length > 100
                ? chunk.Content[..100]
                : chunk.Content;

            if (!seen.Add(contentKey))
                continue;

            if (chunk.RelevanceScore < options.MinRelevanceScore)
                continue;

            result.Add(chunk);

            if (result.Count >= options.TopK)
                break;
        }

        return result;
        }
    }

    private static float CosineSimilarity(float[] a, IReadOnlyList<float> b)
    {
        if (a.Length != b.Count || a.Length == 0)
            return 0f;

        float dotProduct = 0f;
        float normA = 0f;
        float normB = 0f;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0f ? 0f : dotProduct / denominator;
    }
}
