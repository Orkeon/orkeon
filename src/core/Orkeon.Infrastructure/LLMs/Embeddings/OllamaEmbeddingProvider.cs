using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.LLMs.Embeddings;

/// <summary>
/// Embedding provider that calls the Ollama /api/embed endpoint directly via HTTP.
/// </summary>
public class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly EmbeddingOptions _options;

    /// <inheritdoc />
    public string Name => "Ollama";
    /// <inheritdoc />
    public string Model => _options.Model;
    /// <inheritdoc />
    public int Dimensions => _options.Dimension;

    /// <summary>Initializes a new instance of <see cref="OllamaEmbeddingProvider"/>.</summary>
    /// <param name="httpClient">The HTTP client configured for Ollama.</param>
    /// <param name="options">The embedding options.</param>
    public OllamaEmbeddingProvider(HttpClient httpClient, IOptions<EmbeddingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var request = new OllamaEmbedRequest { Model = _options.Model, Input = text };
        var response = await _httpClient.PostAsJsonAsync("/api/embed", request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken).ConfigureAwait(false);
        return result?.Embeddings?.FirstOrDefault() ?? [];
    }

    /// <inheritdoc />
    public async Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken cancellationToken = default)
    {
        var request = new OllamaEmbedBatchRequest { Model = _options.Model, Input = texts.ToList() };
        var response = await _httpClient.PostAsJsonAsync("/api/embed", request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken).ConfigureAwait(false);
        return (IList<float[]>)(result?.Embeddings ?? []);
    }

    private sealed class OllamaEmbedRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("input")]
        public string Input { get; set; } = string.Empty;
    }

    private sealed class OllamaEmbedBatchRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("input")]
        public List<string> Input { get; set; } = [];
    }

    private sealed class OllamaEmbedResponse
    {
        [JsonPropertyName("embeddings")]
        public List<float[]>? Embeddings { get; set; }
    }
}
