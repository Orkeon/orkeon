using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Application.Configuration;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Infrastructure.LLMs.Embeddings;

/// <summary>
/// Decorator that adds in-memory caching to any IEmbeddingProvider.
/// Uses a ConcurrentDictionary-based cache with configurable expiration.
/// </summary>
public sealed class CachedEmbeddingProvider : IEmbeddingProvider
{
    private readonly IEmbeddingProvider _inner;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly TimeSpan _slidingExpiration;

    /// <inheritdoc />
    public string Name => _inner.Name;
    /// <inheritdoc />
    public string Model => _inner.Model;
    /// <inheritdoc />
    public int Dimensions => _inner.Dimensions;

    /// <summary>Initializes a new instance of <see cref="CachedEmbeddingProvider"/>.</summary>
    /// <param name="inner">The underlying embedding provider to wrap.</param>
    /// <param name="options">Cache configuration options.</param>
    public CachedEmbeddingProvider(
        IEmbeddingProvider inner,
        IOptions<EmbeddingCacheOptions> options)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        ArgumentNullException.ThrowIfNull(options);
        var effectiveOptions = options.Value;
        _slidingExpiration = TimeSpan.FromMinutes(effectiveOptions.SlidingExpirationMinutes);
    }

    /// <inheritdoc />
    public async Task<float[]> GetEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var key = BuildCacheKey(text);
        if (TryGetFromCache(key, out var cached))
            return cached!;

        var embedding = await _inner.GetEmbeddingAsync(text, cancellationToken).ConfigureAwait(false);
        AddToCache(key, embedding);
        return embedding;
    }

    /// <inheritdoc />
    public Task<IList<float[]>> GetEmbeddingsAsync(IList<string> texts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return GetEmbeddingsCoreAsync();

        async Task<IList<float[]>> GetEmbeddingsCoreAsync()
        {
            var results = new float[texts.Count][];
            var misses = new List<(int Index, string Text)>();

            for (int i = 0; i < texts.Count; i++)
            {
                var key = BuildCacheKey(texts[i]);
                if (TryGetFromCache(key, out var cached))
                    results[i] = cached!;
                else
                    misses.Add((i, texts[i]));
            }

            if (misses.Count > 0)
            {
                var missTexts = misses.Select(m => m.Text).ToList();
                var embeddings = await _inner.GetEmbeddingsAsync(missTexts, cancellationToken).ConfigureAwait(false);
                for (int j = 0; j < misses.Count; j++)
                {
                    results[misses[j].Index] = embeddings[j];
                    var cacheKey = BuildCacheKey(misses[j].Text);
                    AddToCache(cacheKey, embeddings[j]);
                }
            }

            return results;
        }
    }

    private string BuildCacheKey(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return $"emb:{Model}:{Convert.ToHexString(hash)}";
    }

    private bool TryGetFromCache(string key, out float[]? value)
    {
        if (_cache.TryGetValue(key, out var entry) && !entry.IsExpired(_slidingExpiration))
        {
            entry.Touch();
            value = entry.Value;
            return true;
        }
        value = null;
        return false;
    }

    private void AddToCache(string key, float[] value)
    {
        _cache[key] = new CacheEntry(value);
        EvictExpiredEntries();
    }

    private void EvictExpiredEntries()
    {
        foreach (var key in _cache.Where(kvp => kvp.Value.IsExpired(_slidingExpiration)).Select(kvp => kvp.Key))
        {
            _cache.TryRemove(key, out _);
        }
    }

    private sealed class CacheEntry
    {
        public float[] Value { get; }
        private DateTime _lastAccessed;

        public CacheEntry(float[] value)
        {
            Value = value;
            _lastAccessed = DateTime.UtcNow;
        }

        public void Touch() => _lastAccessed = DateTime.UtcNow;

        public bool IsExpired(TimeSpan slidingExpiration)
            => DateTime.UtcNow - _lastAccessed > slidingExpiration;
    }
}
