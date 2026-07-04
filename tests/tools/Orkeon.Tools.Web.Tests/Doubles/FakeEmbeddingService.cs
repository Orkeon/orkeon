using Orkeon.Domain.Memory;

namespace Orkeon.Tools.Web.Tests.Doubles;

/// <summary>
/// Deterministic in-memory embedding service for tests. Returns a fixed-length
/// vector derived from the input text so different texts get distinct embeddings.
/// </summary>
public sealed class FakeEmbeddingService : IEmbeddingService
{
    public int CallCount { get; private set; }
    public string? LastText { get; private set; }

    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastText = text;

        // Simple, deterministic 4-dimensional vector based on the text.
        var hash = text.GetHashCode(StringComparison.Ordinal);
        var vec = new float[]
        {
            (hash & 0xFF) / 255f,
            ((hash >> 8) & 0xFF) / 255f,
            ((hash >> 16) & 0xFF) / 255f,
            text.Length % 100 / 100f
        };
        return Task.FromResult(vec);
    }
}
