using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Infrastructure.LLMs.Embeddings;

/// <summary>
/// Simple embedding service for development and testing.
/// Uses SHA256 hash to generate deterministic embeddings.
/// </summary>
[Obsolete("Use IEmbeddingProvider implementations (OpenAIEmbeddingProvider, OllamaEmbeddingProvider) instead. Will be removed in v2.0.")]
public partial class SimpleEmbeddingService : IEmbeddingService
{
    private readonly ILogger<SimpleEmbeddingService> _logger;
    private readonly int _dimension;

    /// <summary>Initializes a new instance of <see cref="SimpleEmbeddingService"/>.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="dimension">The dimension of generated embeddings. Defaults to <see cref="EmbeddingDefaults.LocalDimension"/>.</param>
    public SimpleEmbeddingService(ILogger<SimpleEmbeddingService> logger, int dimension = EmbeddingDefaults.LocalDimension)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _dimension = dimension;
    }

    /// <inheritdoc />
    public int Dimension => _dimension;

    /// <inheritdoc />
    public async Task<float[]> GetEmbeddingAsync(string text)
    {
        return await GenerateEmbeddingAsync(text).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (string.IsNullOrWhiteSpace(text))
        {
            return Task.FromResult(new float[_dimension]);
        }

        return GenerateEmbeddingCoreAsync(text, cancellationToken);

        async Task<float[]> GenerateEmbeddingCoreAsync(string text, CancellationToken cancellationToken)
        {
            // Check for cancellation before starting
            cancellationToken.ThrowIfCancellationRequested();

            LogGeneratingEmbeddingForTextOf(text.Length);

            // Simulate async operation
            await Task.Yield();

            // Generate deterministic pseudo-embeddings using hash
            var textBytes = Encoding.UTF8.GetBytes(text);
            var hashBytes = SHA256.HashData(textBytes);

            // Use hash to seed random number generator for deterministic results
            var seed = BitConverter.ToInt32(hashBytes, 0);
#pragma warning disable CA5394 // deterministic SHA256-seeded RNG produces reproducible dev/fallback pseudo-embeddings; a CSPRNG cannot be seeded and would break determinism. Not security-sensitive.
            var random = new Random(seed);

            // Generate embedding vector
            var embedding = new float[_dimension];
            for (int i = 0; i < _dimension; i++)
            {
                // Generate values between -1 and 1
                embedding[i] = (float)(random.NextDouble() * 2 - 1);
            }
#pragma warning restore CA5394

            // Normalize the vector
            var magnitude = (float)Math.Sqrt(embedding.Sum(x => x * x));
            if (magnitude > 0)
            {
                for (int i = 0; i < embedding.Length; i++)
                {
                    embedding[i] /= magnitude;
                }
            }

            return embedding;
        }
    }

    /// <inheritdoc />
    public Task<List<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return GenerateEmbeddingsCoreAsync(texts, cancellationToken);

        async Task<List<float[]>> GenerateEmbeddingsCoreAsync(IEnumerable<string> texts, CancellationToken cancellationToken)
        {
            var embeddings = new List<float[]>();

            foreach (var text in texts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var embedding = await GetEmbeddingAsync(text).ConfigureAwait(false);
                embeddings.Add(embedding);
            }

            LogGeneratedEmbeddings(embeddings.Count);
            return embeddings;
        }
    }

    /// <inheritdoc />
    public float CalculateSimilarity(float[] embedding1, float[] embedding2)
    {
        ArgumentNullException.ThrowIfNull(embedding1);
        ArgumentNullException.ThrowIfNull(embedding2);

        if (embedding1.Length == 0 || embedding2.Length == 0)
        {
            throw new ArgumentException("Embeddings cannot be empty", embedding1.Length == 0 ? nameof(embedding1) : nameof(embedding2));
        }

        if (embedding1.Length != embedding2.Length)
        {
            throw new ArgumentException("Embeddings must have the same dimension", nameof(embedding2));
        }

        // Calculate cosine similarity
        float dotProduct = 0;
        float magnitude1 = 0;
        float magnitude2 = 0;

        for (int i = 0; i < embedding1.Length; i++)
        {
            dotProduct += embedding1[i] * embedding2[i];
            magnitude1 += embedding1[i] * embedding1[i];
            magnitude2 += embedding2[i] * embedding2[i];
        }

        magnitude1 = (float)Math.Sqrt(magnitude1);
        magnitude2 = (float)Math.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
        {
            return 0;
        }

        return dotProduct / (magnitude1 * magnitude2);
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Generating embedding for text of length {Length}")]
    private partial void LogGeneratingEmbeddingForTextOf(int length);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "Generated {Count} embeddings")]
    private partial void LogGeneratedEmbeddings(int count);

}
