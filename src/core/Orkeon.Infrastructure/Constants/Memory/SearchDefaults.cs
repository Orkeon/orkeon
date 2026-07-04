namespace Orkeon.Infrastructure.Constants.Memory;

/// <summary>
/// Default values for memory search operations (similarity thresholds, paging, hybrid scoring).
/// Centralises numeric literals across memory provider implementations.
/// </summary>
public static class SearchDefaults
{
    /// <summary>Default cosine similarity threshold for semantic search results.</summary>
    public const float DefaultSimilarityThreshold = 0.85f;

    /// <summary>Default weight for the keyword component in hybrid search scoring.</summary>
    public const double KeywordWeight = 0.3;

    /// <summary>Smoothing constant (k) used in Reciprocal Rank Fusion (RRF) scoring.</summary>
    public const float RrfConstant = 60f;

    /// <summary>Default page size for paginated memory queries.</summary>
    public const int DefaultPageSize = 100;
}
