namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>Represents a vector similarity metric used for semantic search.</summary>
public sealed record SimilarityMetric
{
    /// <summary>Gets the string value of this similarity metric.</summary>
    public string Value { get; }
    private SimilarityMetric(string value) => Value = value;
    /// <summary>Cosine similarity; measures angle between vectors.</summary>
    public static readonly SimilarityMetric Cosine = new("Cosine");
    /// <summary>Dot product similarity; measures alignment and magnitude.</summary>
    public static readonly SimilarityMetric DotProduct = new("DotProduct");
    /// <summary>Euclidean distance converted to similarity.</summary>
    public static readonly SimilarityMetric Euclidean = new("Euclidean");
    private static readonly Dictionary<string, SimilarityMetric> s_all = new(StringComparer.OrdinalIgnoreCase)
    { [nameof(Cosine)] = Cosine, [nameof(DotProduct)] = DotProduct, [nameof(Euclidean)] = Euclidean };
    /// <summary>Gets all valid similarity metrics.</summary>
    public static IReadOnlyCollection<SimilarityMetric> All => s_all.Values;
    /// <summary>Creates a <see cref="SimilarityMetric"/> from its string representation.</summary>
    public static SimilarityMetric From(string value) => s_all.TryGetValue(value, out var m) ? m : throw new ArgumentException($"Unknown SimilarityMetric: '{value}'", nameof(value));
    /// <summary>Computes the similarity score between two vectors using this metric.</summary>
    public float Compute(ReadOnlySpan<float> a, ReadOnlySpan<float> b) => Value switch
    {
        "Cosine" => VectorMath.CosineSimilarity(a, b),
        "DotProduct" => VectorMath.DotProduct(a, b),
        "Euclidean" => 1f / (1f + VectorMath.EuclideanDistance(a, b)),
        _ => throw new InvalidOperationException($"Unknown metric: {Value}")
    };
    /// <summary>Returns the string representation.</summary>
    public override string ToString() => Value;
    /// <summary>Implicitly converts to string.</summary>
    public static implicit operator string(SimilarityMetric m)
    {
        ArgumentNullException.ThrowIfNull(m);
        return m.Value;
    }
}
