using Orkeon.Domain.Memory;

namespace Orkeon.Tools.FileSystem.Tests.Doubles;

/// <summary>
/// A deterministic mock embedding service for testing.
/// Generates embeddings based on a configurable factory function,
/// or defaults to a simple hash-based approach.
/// </summary>
public sealed class MockEmbeddingService : IEmbeddingService
{
    private Func<string, float[]>? _factory;
    private readonly int _dimensions;

    /// <summary>Number of times GetEmbeddingAsync was called.</summary>
    public int CallCount { get; private set; }

    /// <summary>All texts that were embedded, in order.</summary>
    public List<string> EmbeddedTexts { get; } = [];

    public MockEmbeddingService(int dimensions = 8)
    {
        _dimensions = dimensions;
    }

    /// <summary>
    /// Sets a factory function that produces embeddings from text.
    /// This allows tests to control similarity between specific texts.
    /// </summary>
    public void SetEmbeddingFactory(Func<string, float[]> factory)
    {
        _factory = factory;
    }

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        CallCount++;
        EmbeddedTexts.Add(text);

        if (_factory is not null)
            return Task.FromResult(_factory(text));

        return Task.FromResult(DefaultEmbedding(text));
    }

    /// <summary>
    /// Default embedding: deterministic hash-based approach.
    /// Similar strings will have somewhat similar embeddings.
    /// </summary>
    private float[] DefaultEmbedding(string text)
    {
        var embedding = new float[_dimensions];
        var normalized = text.ToLowerInvariant().Trim();

        for (int i = 0; i < _dimensions; i++)
        {
            float sum = 0;
            for (int j = 0; j < normalized.Length; j++)
            {
                sum += normalized[j] * MathF.Sin((i + 1) * (j + 1) * 0.1f);
            }
            embedding[i] = sum / Math.Max(normalized.Length, 1);
        }

        // Normalize to unit vector
        float magnitude = 0;
        foreach (var v in embedding)
            magnitude += v * v;
        magnitude = MathF.Sqrt(magnitude);

        if (magnitude > 0)
        {
            for (int i = 0; i < _dimensions; i++)
                embedding[i] /= magnitude;
        }

        return embedding;
    }
}
