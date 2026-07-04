using Orkeon.Infrastructure.Constants.Memory;

namespace Orkeon.Infrastructure.Knowledge.Retrieval;

/// <summary>
/// Provides scoring functions for hybrid search combining semantic and keyword matching.
/// </summary>
public static class HybridScorer
{
    /// <summary>
    /// Computes the Reciprocal Rank Fusion score for combining two ranked lists.
    /// </summary>
    /// <param name="semanticRank">Rank position in the semantic search results (1-based).</param>
    /// <param name="keywordRank">Rank position in the keyword search results (1-based).</param>
    /// <param name="k">Smoothing constant (default 60).</param>
    /// <returns>Combined RRF score.</returns>
    public static float ComputeRRFScore(int semanticRank, int keywordRank, float k = SearchDefaults.RrfConstant)
    {
        return (1f / (k + semanticRank)) + (1f / (k + keywordRank));
    }

    /// <summary>
    /// Computes a weighted combination of semantic and keyword scores.
    /// </summary>
    /// <param name="semanticScore">Semantic similarity score (0.0 to 1.0).</param>
    /// <param name="keywordScore">Keyword match score (0.0 to 1.0).</param>
    /// <param name="semanticWeight">Weight for the semantic score.</param>
    /// <param name="keywordWeight">Weight for the keyword score.</param>
    /// <returns>Weighted combined score.</returns>
    public static float ComputeWeightedScore(
        float semanticScore, float keywordScore,
        float semanticWeight = 0.7f, float keywordWeight = (float)SearchDefaults.KeywordWeight)
    {
        return semanticScore * semanticWeight + keywordScore * keywordWeight;
    }

    /// <summary>
    /// Computes a simple keyword overlap score between query terms and a document.
    /// </summary>
    /// <param name="query">The search query.</param>
    /// <param name="document">The document text to score.</param>
    /// <returns>Ratio of query terms found in the document (0.0 to 1.0).</returns>
    public static float ComputeKeywordScore(string query, string document)
    {
        ArgumentNullException.ThrowIfNull(query);
        var queryTerms = query.Split(' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (queryTerms.Length == 0)
            return 0f;

        int matches = queryTerms.Count(term => document.Contains(term, StringComparison.OrdinalIgnoreCase));

        return (float)matches / queryTerms.Length;
    }
}
