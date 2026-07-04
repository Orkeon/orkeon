using Orkeon.Domain.Memory;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Orkeon.Infrastructure.Tests.TestDoubles;

/// <summary>
/// Test implementation of a Redis-like memory provider that uses in-memory storage.
/// This avoids the need for real Redis connections in tests.
/// </summary>
public sealed class TestRedisMemoryProvider : IMemoryProvider, IDisposable
{
    private readonly ILogger<TestRedisMemoryProvider>? _logger;
    private readonly ConcurrentDictionary<string, string> _storage = new();
    private readonly ConcurrentDictionary<string, MemoryItem> _memoryItems = new();
    private readonly HashSet<string> _keys = [];
    private readonly string _keyPrefix = "orkeon:memory:";
    private bool _initialized = true;

    public TestRedisMemoryProvider(ILogger<TestRedisMemoryProvider>? logger = null)
    {
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _initialized = true;
        if (_logger?.IsEnabled(LogLevel.Information) == true) _logger.LogInformation("Test Redis memory provider initialized");
        return Task.CompletedTask;
    }

    public async Task StoreAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key), "Key cannot be null or empty");
        ArgumentNullException.ThrowIfNull(item);

        var redisKey = $"{_keyPrefix}{key}";

        // Store as JSON like real Redis implementation
        var json = JsonSerializer.Serialize(new
        {
            Id = item.Id,
            Content = item.Content,
            Timestamp = item.Timestamp,
            LastAccessed = item.LastAccessed,
            AccessCount = item.AccessCount,
            Importance = item.Importance,
            Source = item.Source,
            Tags = item.Tags,
            Metadata = ConvertMetadataToDict(item.Metadata?.CustomProperties),
            HasEmbedding = item.Embedding != null && item.Embedding.Count > 0
        });

        _storage[redisKey] = json;
        _memoryItems[key] = item;
        _keys.Add(key);

        if (_logger?.IsEnabled(LogLevel.Debug) == true) _logger.LogDebug("Stored memory item with key: {Key}", key);
    }

    public async Task<MemoryItem?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrEmpty(key))
            throw new ArgumentNullException(nameof(key), "Key cannot be null or empty");

        if (_memoryItems.TryGetValue(key, out var item))
        {
            if (_logger?.IsEnabled(LogLevel.Debug) == true) _logger.LogDebug("Retrieved memory item with key: {Key}", key);
            return item;
        }

        var redisKey = $"{_keyPrefix}{key}";
        if (_storage.TryGetValue(redisKey, out var json))
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var content = root.GetProperty("Content").GetString() ?? string.Empty;
                var importance = root.TryGetProperty("Importance", out var impEl) ? (float)impEl.GetDouble() : 0.5f;
                var source = root.TryGetProperty("Source", out var srcEl) ? srcEl.GetString() : "redis";

                Dictionary<string, string>? tags = null;
                if (root.TryGetProperty("Tags", out var tagsEl) && tagsEl.ValueKind != JsonValueKind.Null)
                {
                    tags = JsonSerializer.Deserialize<Dictionary<string, string>>(tagsEl.GetRawText());
                }

                Dictionary<string, string>? metadata = null;
                if (root.TryGetProperty("Metadata", out var metadataElement) && metadataElement.ValueKind != JsonValueKind.Null)
                {
                    metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataElement.GetRawText());
                }

                var memoryItem = MemoryItem.Create(
                    content: content,
                    embedding: null,
                    importance: importance,
                    source: source,
                    tags: tags?.Select(kvp => $"{kvp.Key}:{kvp.Value}").ToArray(),
                    createdBy: null,
                    customProperties: metadata
                );

                // Update access count if stored
                if (root.TryGetProperty("AccessCount", out var accessCountEl))
                {
                    var accessCount = accessCountEl.GetInt32();
                    for (int i = 0; i < accessCount; i++)
                    {
                        memoryItem.IncrementAccessCount();
                    }
                }

                if (_logger?.IsEnabled(LogLevel.Debug) == true) _logger.LogDebug("Retrieved memory item with key: {Key}", key);
                return memoryItem;
            }
            catch (Exception ex)
            {
                if (_logger?.IsEnabled(LogLevel.Error) == true) _logger.LogError(ex, "Failed to deserialize memory item for key: {Key}", key);
                return null;
            }
        }

        if (_logger?.IsEnabled(LogLevel.Debug) == true) _logger.LogDebug("Memory item not found for key: {Key}", key);
        return null;
    }

    public async Task<IEnumerable<MemoryItem>> SearchAsync(string query, int limit = 10, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrEmpty(query))
            throw new ArgumentNullException(nameof(query), "Query cannot be null or empty");

        var results = new List<MemoryItem>();

        foreach (var key in _keys.Take(100)) // Limit scan to first 100 keys
        {
            var item = await GetAsync(key, cancellationToken);
            if (item != null && item.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                results.Add(item);
                if (results.Count >= limit)
                    break;
            }
        }

        if (_logger?.IsEnabled(LogLevel.Debug) == true) _logger.LogDebug("Search for '{Query}' returned {Count} results", query, results.Count);
        return results.OrderByDescending(i => i.Importance).Take(limit);
    }

    public async Task<bool> UpdateAsync(string key, MemoryItem item, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(item);

        if (_keys.Contains(key))
        {
            await StoreAsync(key, item, cancellationToken);
            if (_logger?.IsEnabled(LogLevel.Debug) == true) _logger.LogDebug("Updated memory item with key: {Key}", key);
            return true;
        }

        if (_logger?.IsEnabled(LogLevel.Debug) == true) _logger.LogDebug("Memory item not found for update with key: {Key}", key);
        return false;
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        ArgumentNullException.ThrowIfNull(key);

        var redisKey = $"{_keyPrefix}{key}";
        var removed = _storage.TryRemove(redisKey, out _);
        _memoryItems.TryRemove(key, out _);
        _keys.Remove(key);

        if (removed)
        {
            if (_logger?.IsEnabled(LogLevel.Debug) == true) _logger.LogDebug("Deleted memory item with key: {Key}", key);
            return true;
        }

        if (_logger?.IsEnabled(LogLevel.Debug) == true) _logger.LogDebug("Memory item not found for deletion with key: {Key}", key);
        return false;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        _storage.Clear();
        _memoryItems.Clear();
        _keys.Clear();

        if (_logger?.IsEnabled(LogLevel.Information) == true) _logger.LogInformation("Cleared all memory items");
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized)
        {
            await InitializeAsync(cancellationToken);
        }
    }

    private static Dictionary<string, string>? ConvertMetadataToDict(Dictionary<string, string>? customProperties)
    {
        return customProperties;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _storage.Clear();
        _memoryItems.Clear();
        _keys.Clear();
        _initialized = false;
    }
}
