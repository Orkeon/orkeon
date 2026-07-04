using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Analysis.Abstractions.Interfaces;

namespace Orkeon.Analysis.Vectors;

public sealed partial class OpenAIEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    private readonly HttpClient _http;
    private readonly OpenAIEmbeddingOptions _options;
    private readonly bool _ownsClient;
    private readonly ILogger<OpenAIEmbeddingProvider> _logger;

    public OpenAIEmbeddingProvider(
        OpenAIEmbeddingOptions options,
        HttpClient? client = null,
        ILogger<OpenAIEmbeddingProvider>? logger = null)
        : this(options, client, handler: null, logger)
    {
    }

    /// <summary>
    /// Constructor that accepts an optional <see cref="HttpMessageHandler"/> for intercepting HTTP calls
    /// (e.g. logging via <c>LlmLoggingDelegatingHandler</c>).
    /// When <paramref name="handler"/> is non-null a new <see cref="HttpClient"/> is created with it.
    /// </summary>
    public OpenAIEmbeddingProvider(
        OpenAIEmbeddingOptions options,
        HttpMessageHandler? handler,
        ILogger<OpenAIEmbeddingProvider>? logger = null)
        : this(options, client: null, handler: handler, logger)
    {
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "The SocketsHttpHandler is owned by the HttpClient created with disposeHandler:true and stored in the long-lived _http field; it is disposed transitively when this provider disposes _http (when _ownsClient is true).")]
    private OpenAIEmbeddingProvider(
        OpenAIEmbeddingOptions options,
        HttpClient? client,
        HttpMessageHandler? handler,
        ILogger<OpenAIEmbeddingProvider>? logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrEmpty(options.ApiKey);
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
        if (!_http.DefaultRequestHeaders.Contains("Authorization"))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer {options.ApiKey}");
        }
        _logger = logger ?? NullLogger<OpenAIEmbeddingProvider>.Instance;
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

        var effectiveTexts = ApplyTruncationIfConfigured(texts);

        var results = new List<ReadOnlyMemory<float>>(effectiveTexts.Count);
        for (var offset = 0; offset < effectiveTexts.Count; offset += _options.BatchSize)
        {
            var chunk = effectiveTexts.Skip(offset).Take(_options.BatchSize).ToList();
            var vectors = await EmbedChunkWithRetryAsync(chunk, ct).ConfigureAwait(false);
            results.AddRange(vectors);
        }
        return results;
    }

    private IReadOnlyList<string> ApplyTruncationIfConfigured(IReadOnlyList<string> texts)
    {
        if (_options.MaxTextChars is not { } maxChars || maxChars <= 0)
            return texts;

        var truncated = new List<string>(texts.Count);
        foreach (var t in texts)
            truncated.Add(t.Length > maxChars ? t[..maxChars] : t);
        return truncated;
    }

    private async Task<List<ReadOnlyMemory<float>>> EmbedChunkWithRetryAsync(
        List<string> chunk,
        CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return await EmbedChunkAsync(chunk, ct).ConfigureAwait(false);
            }
            catch (EmbeddingPayloadTooLargeException) when (chunk.Count > 1)
            {
                // Backend rejected the chunk because at least one text exceeds the
                // physical batch / context limit (e.g. llama.cpp default 512 tokens).
                // Binary-split to isolate the offender(s) without losing siblings.
                var mid = chunk.Count / 2;
                LogBinarySplit(chunk.Count, mid, chunk.Count - mid);
                var first = await EmbedChunkWithRetryAsync(chunk.Take(mid).ToList(), ct).ConfigureAwait(false);
                var second = await EmbedChunkWithRetryAsync(chunk.Skip(mid).ToList(), ct).ConfigureAwait(false);
                var merged = new List<ReadOnlyMemory<float>>(chunk.Count);
                merged.AddRange(first);
                merged.AddRange(second);
                return merged;
            }
            catch (EmbeddingPayloadTooLargeException) when (chunk.Count == 1
                && chunk[0].Length > _options.MinTruncationChars)
            {
                // Single oversized text: bisect the content (keep head — most signal
                // for code: imports, type defs, JSDoc) and retry. Bounded by
                // MinTruncationChars to avoid infinite recursion.
                var originalLength = chunk[0].Length;
                var halved = chunk[0][..(originalLength / 2)];
                LogTextBisection(originalLength, halved.Length);
                chunk = [halved];
                // No attempt++ here: this is a corrective re-shape, not a transient retry.
                continue;
            }
            catch (HttpRequestException) when (attempt < _options.MaxRetries)
            {
                attempt++;
                await Task.Delay(TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt)), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<List<ReadOnlyMemory<float>>> EmbedChunkAsync(
        IReadOnlyList<string> chunk,
        CancellationToken ct)
    {
        var body = new OpenAIEmbeddingRequest { Model = _options.Model, Input = chunk.ToArray() };
        using var request = new HttpRequestMessage(HttpMethod.Post, _options.EmbeddingsPath)
        {
            Content = JsonContent.Create(body),
        };
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if ((int)response.StatusCode >= 500)
        {
            // Distinguish "deterministic too-large" 500s (llama.cpp / vLLM physical
            // batch overflow) from genuinely transient 5xx — the former MUST NOT be
            // retried as-is, otherwise the circuit breaker trips after N identical
            // failures (R25 incident on lexical).
            var errBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (IsPayloadTooLargeError(errBody))
                throw new EmbeddingPayloadTooLargeException(errBody);
        }
        if (response.StatusCode == HttpStatusCode.TooManyRequests
            || (int)response.StatusCode >= 500)
        {
            throw new HttpRequestException($"OpenAI embeddings transient error: {(int)response.StatusCode}");
        }
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OpenAIEmbeddingResponse>(cancellationToken: ct).ConfigureAwait(false);
        if (payload is null || payload.Data is null) return [];
        return payload.Data
            .OrderBy(d => d.Index)
            .Select(d => new ReadOnlyMemory<float>(d.Embedding ?? []))
            .ToList();
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Embedding payload too large — binary-split chunk of {TotalCount} texts into {FirstHalf} + {SecondHalf}")]
    private partial void LogBinarySplit(int totalCount, int firstHalf, int secondHalf);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Embedding payload too large — bisecting single text from {OriginalChars} → {HalvedChars} chars")]
    private partial void LogTextBisection(int originalChars, int halvedChars);

    private static bool IsPayloadTooLargeError(string body)
    {
        if (string.IsNullOrEmpty(body)) return false;
        // Patterns observed across backends:
        //   llama.cpp:   "input (603 tokens) is too large to process. increase the physical batch size (current batch size: 512)"
        //   vLLM:        "Input length (... tokens) exceeds maximum context length"
        //   Generic:     "context length", "too long", "exceeds maximum"
        return body.Contains("too large to process", StringComparison.OrdinalIgnoreCase)
            || body.Contains("physical batch size", StringComparison.OrdinalIgnoreCase)
            || body.Contains("exceeds maximum context", StringComparison.OrdinalIgnoreCase)
            || body.Contains("input is too long", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }

    private sealed class OpenAIEmbeddingRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("input")] public string[] Input { get; set; } = [];
    }

    private sealed class OpenAIEmbeddingResponse
    {
        [JsonPropertyName("data")] public List<Item>? Data { get; set; }

        public sealed class Item
        {
            [JsonPropertyName("index")] public int Index { get; set; }
            [JsonPropertyName("embedding")] public float[]? Embedding { get; set; }
        }
    }
}

public sealed record OpenAIEmbeddingOptions
{
    public required string ApiKey { get; init; }
    public Uri BaseUrl { get; init; } = new("https://api.openai.com/");
    public string EmbeddingsPath { get; init; } = "v1/embeddings";
    /// <summary>
    /// OpenAI embedding model identifier. Default <c>text-embedding-3-small</c> (1536 dims).
    /// This default is provider-specific (it identifies the OpenAI model), not a generic
    /// embedding dimension assumption — other providers expose their own defaults.
    /// </summary>
    public string Model { get; init; } = "text-embedding-3-small";

    /// <summary>
    /// Vector dimension produced by the configured OpenAI model. Default 1536 matches
    /// <c>text-embedding-3-small</c>. Override when using <c>text-embedding-3-large</c>
    /// (3072) or a custom dim via OpenAI's <c>dimensions</c> request parameter.
    /// </summary>
    public int Dimensions { get; init; } = 1536;
    public int BatchSize { get; init; } = 16;
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Per-text character cap applied before batching. Texts longer than this are truncated.
    /// Protects against backends with a fixed physical_batch_size (e.g. llama.cpp default
    /// 512 tokens) that would 500 on long inputs. Rule of thumb: pick ~1× the token budget
    /// (conservative — assumes worst-case 1 char/token on dense code). <see langword="null"/>
    /// disables truncation.
    /// </summary>
    public int? MaxTextChars { get; init; }

    /// <summary>
    /// Lower bound for the on-the-fly bisection performed when a single text triggers
    /// the backend's "input too large" error. Once a halved text falls below this size
    /// the provider stops bisecting and rethrows — protects against pathological backends
    /// that reject every payload regardless of size.
    /// </summary>
    public int MinTruncationChars { get; init; } = 256;
}

/// <summary>
/// Raised when the embedding backend deterministically rejects a payload as too large
/// (e.g. llama.cpp <c>physical_batch_size</c> overflow). Distinct from
/// <see cref="HttpRequestException"/> because retrying the same payload is futile —
/// the caller must re-shape (split or truncate) before retrying.
/// </summary>
public sealed class EmbeddingPayloadTooLargeException : Exception
{
    /// <summary>Initializes a new instance with no message. Present for framework compatibility.</summary>
    public EmbeddingPayloadTooLargeException()
    {
        ServerMessage = string.Empty;
    }

    /// <summary>Initializes a new instance with the raw server error message.</summary>
    /// <param name="serverMessage">Error body returned by the embedding backend.</param>
    public EmbeddingPayloadTooLargeException(string serverMessage)
        : base($"Embedding backend rejected payload as too large: {serverMessage}")
    {
        ServerMessage = serverMessage ?? string.Empty;
    }

    /// <summary>Initializes a new instance with the raw server error message and an inner exception.</summary>
    /// <param name="serverMessage">Error body returned by the embedding backend.</param>
    /// <param name="innerException">The exception that caused this error, if any.</param>
    public EmbeddingPayloadTooLargeException(string serverMessage, Exception? innerException)
        : base($"Embedding backend rejected payload as too large: {serverMessage}", innerException)
    {
        ServerMessage = serverMessage ?? string.Empty;
    }

    /// <summary>Raw error body returned by the embedding backend.</summary>
    public string ServerMessage { get; }
}
