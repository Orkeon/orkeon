namespace Orkeon.Domain.SharedKernel.ValueObjects;

/// <summary>
/// Provides vector math operations for similarity search computations.
/// </summary>
public static class VectorMath
{
    /// <summary>
    /// Computes cosine similarity between two vectors.
    /// Returns a value between -1 and 1, where 1 means identical direction.
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Vector dimensions mismatch: {a.Length} vs {b.Length}", nameof(b));

        float dotProduct = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        var denominator = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denominator == 0 ? 0f : dotProduct / denominator;
    }

    /// <summary>
    /// Computes the dot product of two vectors.
    /// </summary>
    public static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Vector dimensions mismatch: {a.Length} vs {b.Length}", nameof(b));

        float result = 0;
        for (int i = 0; i < a.Length; i++)
            result += a[i] * b[i];
        return result;
    }

    /// <summary>
    /// Computes the Euclidean distance between two vectors.
    /// </summary>
    public static float EuclideanDistance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException($"Vector dimensions mismatch: {a.Length} vs {b.Length}", nameof(b));

        float sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            var diff = a[i] - b[i];
            sum += diff * diff;
        }
        return MathF.Sqrt(sum);
    }

    /// <summary>
    /// Normalizes a vector to unit length.
    /// </summary>
    public static float[] Normalize(float[] vector)
    {
        var norm = MathF.Sqrt(vector.Sum(v => v * v));
        if (norm == 0) return vector;
        return vector.Select(v => v / norm).ToArray();
    }
}
