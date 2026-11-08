using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;
using static Orkeon.Domain.Constants.Llm.EmbeddingDefaults;

namespace Orkeon.Infrastructure.LLMs.Embeddings;

/// <summary>
/// Common base of the embedding providers that speak the OpenAI embeddings wire format
/// (<c>POST {"model", "input": [...]}</c> answered by <c>{"data": [{"index", "embedding"}]}</c>):
/// Mistral, Azure OpenAI and Gemini differ only in their URL, their authentication header and their defaults.
/// </summary>
public abstract class OpenAICompatibleEmbeddingProviderBase : IEmbeddingProvider
{
    private const int MaxErrorBodyChars = 300;

    private readonly HttpClient _httpClient;

    /// <summary>Initializes the provider.</summary>
    /// <param name="httpClient">The HTTP client; the provider sets no base address on it.</param>
    /// <param name="options">The embedding options.</param>
    protected OpenAICompatibleEmbeddingProviderBase(HttpClient httpClient, IOptions<EmbeddingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = httpClient;
        Settings = options.Value;
    }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public string Model => Settings.Model == DefaultModel ? ProviderDefaultModel : Settings.Model;

    /// <inheritdoc />
    public int Dimensions => Settings.Dimension == DefaultDimension ? ProviderDefaultDimension : Settings.Dimension;

    /// <summary>The embedding options as bound.</summary>
    protected EmbeddingOptions Settings { get; }

    /// <summary>The model this provider uses when the options keep the global default.</summary>
    protected abstract string ProviderDefaultModel { get; }

    /// <summary>The dimensions of <see cref="ProviderDefaultModel"/>.</summary>
    protected abstract int ProviderDefaultDimension { get; }

    /// <inheritdoc />
    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var all = await GetEmbeddingsAsync([text], cancellationToken).ConfigureAwait(false);
        return all[0];
    }

    /// <inheritdoc />
    public async Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var apiKey = ResolveApiKey();
        var uri = BuildRequestUri();
        var batchSize = Math.Max(1, Settings.BatchSize);
        var result = new List<float[]>(texts.Count);
        for (var offset = 0; offset < texts.Count; offset += batchSize)
        {
            var batch = texts.Skip(offset).Take(batchSize).ToList();
            result.AddRange(await EmbedBatchAsync(uri, apiKey, batch, cancellationToken).ConfigureAwait(false));
        }

        return result;
    }

    /// <summary>
    /// Refuses, before any call, a <c>Dimension</c> the model cannot honour: these providers send no output
    /// size, so a fixed-size model answers its own whatever the setting says. The global default counts as unset.
    /// </summary>
    /// <param name="fixedModel">The model whose size is fixed.</param>
    /// <param name="fixedDimension">That size.</param>
    private protected void RequireFixedDimension(string fixedModel, int fixedDimension)
    {
        if (Model == fixedModel && Settings.Dimension != DefaultDimension && Settings.Dimension != fixedDimension)
        {
            throw new InvalidOperationException(
                $"{Name} model '{fixedModel}' has {fixedDimension} dimensions and Orkeon:Embeddings:Dimension is " +
                $"{Settings.Dimension}: set Dimension to {fixedDimension}, or leave it out. The size is not configurable.");
        }
    }

    /// <summary>The URL the embeddings request is posted to.</summary>
    protected abstract Uri BuildRequestUri();

    /// <summary>Puts the credentials on the request.</summary>
    protected abstract void Authenticate(HttpRequestMessage request, string apiKey);

    private async Task<List<float[]>> EmbedBatchAsync(
        Uri uri, string apiKey, List<string> batch, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = JsonContent.Create(new EmbeddingsRequest { Model = Model, Input = batch }),
        };
        Authenticate(request, apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (body.Length > MaxErrorBodyChars)
                body = body[..MaxErrorBodyChars];
            throw new HttpRequestException(
                $"{Name} embeddings request failed with HTTP {(int)response.StatusCode} ({response.StatusCode}): {body}",
                null,
                response.StatusCode);
        }

        var answer = await response.Content.ReadFromJsonAsync<EmbeddingsResponse>(cancellationToken).ConfigureAwait(false);
        var data = answer?.Data ?? [];
        if (data.Count != batch.Count)
        {
            throw new InvalidOperationException(
                $"{Name} returned {data.Count} embeddings for {batch.Count} inputs.");
        }

        var vectors = data.OrderBy(d => d.Index).Select(d => d.Embedding ?? []).ToList();
        foreach (var vector in vectors)
        {
            if (vector.Length != Dimensions)
            {
                throw new InvalidOperationException(
                    $"{Name} model '{Model}' returned vectors of {vector.Length} dimensions but " +
                    $"Orkeon:Embeddings:Dimension is {Dimensions}. Set Dimension to the model's size: " +
                    "stored vectors depend on it.");
            }
        }

        return vectors;
    }

    private string ResolveApiKey()
    {
        var key = Settings.ApiKey;
        if (string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(Settings.ApiKeyEnvVar))
            key = Environment.GetEnvironmentVariable(Settings.ApiKeyEnvVar);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                $"{Name} embeddings need an API key: set Orkeon:Embeddings:ApiKey, or Orkeon:Embeddings:ApiKeyEnvVar " +
                "to the name of an environment variable that holds it.");
        }

        return key;
    }

    private sealed class EmbeddingsRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("input")]
        public List<string> Input { get; set; } = [];
    }

    private sealed class EmbeddingsResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingItem>? Data { get; set; }
    }

    private sealed class EmbeddingItem
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
