using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.LLMs.Embeddings;

/// <summary>
/// Embedding provider that wraps Microsoft.Extensions.AI IEmbeddingGenerator.
/// Works with any M.E.AI-compatible embedding generator (OpenAI, Azure, etc.).
/// </summary>
public class OpenAIEmbeddingProvider : IEmbeddingProvider
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly EmbeddingOptions _options;

    /// <inheritdoc />
    public string Name => "OpenAI";
    /// <inheritdoc />
    public string Model => _options.Model;
    /// <inheritdoc />
    public int Dimensions => _options.Dimension;

    /// <summary>Initializes a new instance of <see cref="OpenAIEmbeddingProvider"/>.</summary>
    /// <param name="generator">The M.E.AI embedding generator.</param>
    /// <param name="options">The embedding options.</param>
    public OpenAIEmbeddingProvider(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        IOptions<EmbeddingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(generator);
        _generator = generator;
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await _generator.GenerateAsync(
            [text],
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return result[0].Vector.ToArray();
    }

    /// <inheritdoc />
    public async Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken cancellationToken = default)
    {
        var results = await _generator.GenerateAsync(
            texts.ToList(),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return results.Select(r => r.Vector.ToArray()).ToList();
    }
}
