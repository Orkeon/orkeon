using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Ports;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Domain.Constants.Llm;

namespace Orkeon.Infrastructure.Stubs;

/// <summary>
/// Explicit test double for <see cref="IEmbeddingProvider"/> that generates deterministic
/// hash-based embeddings (no semantic signal). Logs a warning on first use.
/// </summary>
/// <remarks>
/// RAG-01/C4: this stub is <b>never resolved implicitly</b> — <c>AddOrkeonInfrastructure()</c>
/// no longer registers it. The default port resolution is semantic-first
/// (<see cref="Orkeon.Infrastructure.LLMs.Embeddings.DefaultEmbeddingProviderResolver"/>:
/// local BGE → configured remote → fail-fast). Register this class explicitly
/// (<c>services.AddSingleton&lt;IEmbeddingProvider, HashBasedEmbeddingProvider&gt;()</c>)
/// only in tests or deterministic offline scenarios where semantics do not matter.
/// </remarks>
public sealed partial class HashBasedEmbeddingProvider : IEmbeddingProvider
{
    private readonly ILogger<HashBasedEmbeddingProvider> _logger;
    private int _warnedOnce;

    /// <inheritdoc />
    public string Name => "hash-based-fallback";

    /// <inheritdoc />
    public string Model => "sha256-hash";

    /// <inheritdoc />
    public int Dimensions { get; }

    /// <summary>Initializes a new instance of <see cref="HashBasedEmbeddingProvider"/>.</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="dimensions">The embedding dimension. Defaults to <see cref="EmbeddingDefaults.LocalDimension"/>.</param>
    public HashBasedEmbeddingProvider(ILogger<HashBasedEmbeddingProvider> logger, int dimensions = EmbeddingDefaults.LocalDimension)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        Dimensions = dimensions;
    }

    private void WarnOnce()
    {
        if (Interlocked.Exchange(ref _warnedOnce, 1) == 0)
            LogHashBasedFallback();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Using HashBasedEmbeddingProvider (explicitly registered test double) — embeddings are " +
            "hash-based, not semantic. For real semantics add AddOrkeonLocalEmbeddings() or configure Orkeon:Embeddings.")]
    private partial void LogHashBasedFallback();

    /// <inheritdoc />
    public Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        WarnOnce();
        return Task.FromResult(GenerateHashEmbedding(text));
    }

    /// <inheritdoc />
    public Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken cancellationToken = default)
    {
        WarnOnce();
        IList<float[]> results = texts.Select(GenerateHashEmbedding).ToList();
        return Task.FromResult(results);
    }

    private float[] GenerateHashEmbedding(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var embedding = new float[Dimensions];
        for (int i = 0; i < Dimensions; i++)
        {
            embedding[i] = (hash[i % hash.Length] / 255f) * 2f - 1f;
        }
        return embedding;
    }
}
