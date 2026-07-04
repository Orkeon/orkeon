namespace Orkeon.Application.Interfaces.Ports
{
    /// <summary>
    /// Interface for generating text embeddings
    /// </summary>
    public interface IEmbeddingProvider
    {
        /// <summary>
        /// Generate embeddings for a single text
        /// </summary>
        System.Threading.Tasks.Task<float[]> GetEmbeddingAsync(
            string text,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Generate embeddings for multiple texts
        /// </summary>
        System.Threading.Tasks.Task<IList<float[]>> GetEmbeddingsAsync(
            IList<string> texts,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Provider name
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Model used for embeddings
        /// </summary>
        string Model { get; }

        /// <summary>
        /// Dimension of the embeddings
        /// </summary>
        int Dimensions { get; }
    }
}
