using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Application.Configuration;

/// <summary>
/// Configuration options for vector similarity search.
/// </summary>
public class VectorSearchOptions
{
    /// <summary>
    /// The default similarity metric to use for vector comparisons.
    /// </summary>
    public SimilarityMetric DefaultMetric { get; set; } = SimilarityMetric.Cosine;

    /// <summary>
    /// The default number of top results to return.
    /// </summary>
    public int DefaultTopK { get; set; } = 10;

    /// <summary>
    /// The default minimum similarity score threshold.
    /// </summary>
    public float DefaultMinScore { get; set; } = (float)SearchDefaults.DefaultSimilarityThreshold;

    /// <summary>
    /// Whether to prefer vector search over text-based search when available.
    /// </summary>
    public bool PreferVectorSearch { get; set; } = true;
}
