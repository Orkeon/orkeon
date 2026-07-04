namespace Orkeon.Domain.Memory;

/// <summary>
/// Domain interface for text embedding services.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Gets embeddings for the given text.
    /// </summary>
    Task<float[]> GetEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default);
}
