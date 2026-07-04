namespace Orkeon.Domain.Memory;

/// <summary>
/// Represents an embedding vector with cosine similarity computation.
/// </summary>
public record EmbeddingVector(IReadOnlyList<float> Values)
{
    /// <summary>
    /// Gets the number of dimensions of the embedding vector.
    /// </summary>
    public int Dimension => Values.Count;

    /// <summary>
    /// Computes the cosine similarity between this vector and another.
    /// Returns a value between -1 and 1, where 1 means identical direction.
    /// </summary>
    public float CosineSimilarity(EmbeddingVector other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.Dimension != Dimension)
            return 0; // Different dimensions = no similarity

        float dotProduct = 0;
        float magnitudeA = 0;
        float magnitudeB = 0;

        for (int i = 0; i < Dimension; i++)
        {
            dotProduct += Values[i] * other.Values[i];
            magnitudeA += Values[i] * Values[i];
            magnitudeB += other.Values[i] * other.Values[i];
        }

        var denominator = (float)(Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));

        // Handle zero magnitude vectors
        if (denominator == 0)
            return 0;

        return dotProduct / denominator;
    }
}
