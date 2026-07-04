using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Analysis.Vectors;

public sealed class OllamaEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly HttpClient _http;
    private readonly OllamaEmbeddingOptions _options;
    private readonly bool _ownsClient;

    public OllamaEmbeddingProvider(OllamaEmbeddingOptions options, HttpClient? client = null)
        : this(options, client, handler: null)
    {
    }

    /// <summary>
    /// Constructor that accepts an optional <see cref="HttpMessageHandler"/> for intercepting HTTP calls
    /// (e.g. logging via <c>LlmLoggingDelegatingHandler</c>).
    /// When <paramref name="handler"/> is non-null a new <see cref="HttpClient"/> is created with it.
    /// </summary>
    public OllamaEmbeddingProvider(OllamaEmbeddingOptions options, HttpMessageHandler? handler)
        : this(options, client: null, handler: handler)
    {
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "The SocketsHttpHandler is owned by the HttpClient created with disposeHandler:true and stored in the long-lived _http field; it is disposed transitively when this provider disposes _http (when _ownsClient is true).")]
    private OllamaEmbeddingProvider(OllamaEmbeddingOptions options, HttpClient? client, HttpMessageHandler? handler)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (client is not null)
        {
            _http = client;
            _ownsClient = false;
        }
        else if (handler is not null)
        {
            _http = new HttpClient(handler) { BaseAddress = options.BaseUrl };
            _ownsClient = true;
        }
        else
        {
            // PooledConnectionLifetime recycles pooled connections so this self-owned
            // fallback client picks up DNS changes on long-lived agent processes (ANT-013).
            _http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
            {
                BaseAddress = options.BaseUrl
            };
            _ownsClient = true;
        }
    }

    public int Dimensions => _options.Dimensions;

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return EmbedBatchCoreAsync(texts, ct);
    }

    private async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchCoreAsync(
        IReadOnlyList<string> texts,
        CancellationToken ct)
    {
        if (texts.Count == 0) return [];

        var results = new List<ReadOnlyMemory<float>>(texts.Count);
        foreach (var text in texts)
        {
            ct.ThrowIfCancellationRequested();
            var body = new OllamaEmbeddingRequest { Model = _options.Model, Prompt = text };
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.EmbeddingsPath)
            {
                Content = JsonContent.Create(body),
            };
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>(cancellationToken: ct).ConfigureAwait(false);
            results.Add(new ReadOnlyMemory<float>(payload?.Embedding ?? []));
        }
        return results;
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }

    private sealed class OllamaEmbeddingRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
    }

    private sealed class OllamaEmbeddingResponse
    {
        [JsonPropertyName("embedding")] public float[]? Embedding { get; set; }
    }
}

public sealed record OllamaEmbeddingOptions
{
    public Uri BaseUrl { get; init; } = new("http://localhost:11434/");
    public string EmbeddingsPath { get; init; } = "api/embeddings";
    public string Model { get; init; } = "nomic-embed-text";

    /// <summary>
    /// Vector dimension produced by the configured Ollama model. Default 768 matches
    /// <c>nomic-embed-text</c>. Override for <c>mxbai-embed-large</c> (1024),
    /// <c>all-minilm</c> (384), or any other Ollama embedding model.
    /// </summary>
    public int Dimensions { get; init; } = 768;
}
