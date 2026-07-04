namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Domain interface for generating text embeddings.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generates an embedding vector for the given text.
    /// </summary>
    System.Threading.Tasks.Task<float[]> GetEmbeddingAsync(string text);
}
