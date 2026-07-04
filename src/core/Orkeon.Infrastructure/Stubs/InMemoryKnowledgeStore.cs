using Microsoft.Extensions.Logging;
using Orkeon.Domain.Knowledge;
using System.Collections.Concurrent;

namespace Orkeon.Infrastructure.Stubs;

/// <summary>
/// In-memory stub implementation of <see cref="IKnowledgeStore"/>.
/// Logs a warning on first use to indicate no persistent knowledge store is configured.
/// </summary>
public sealed partial class InMemoryKnowledgeStore : IKnowledgeStore
{
    private readonly ConcurrentDictionary<string, object> _store = new();
    private readonly ILogger<InMemoryKnowledgeStore> _logger;
    private int _warnedOnce;

    /// <summary>Initializes a new instance of <see cref="InMemoryKnowledgeStore"/>.</summary>
    public InMemoryKnowledgeStore(ILogger<InMemoryKnowledgeStore> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    private void WarnOnce()
    {
        if (Interlocked.Exchange(ref _warnedOnce, 1) == 0)
            LogInMemoryFallback();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Using InMemoryKnowledgeStore — knowledge is not persisted. Register a persistent IKnowledgeStore for production.")]
    private partial void LogInMemoryFallback();

    /// <inheritdoc />
    public Task<bool> StoreAsync(string key, object knowledge, CancellationToken cancellationToken = default)
    {
        WarnOnce();
        _store[key] = knowledge;
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<T?> RetrieveAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        WarnOnce();
        if (_store.TryGetValue(key, out var value) && value is T typed)
            return Task.FromResult<T?>(typed);
        return Task.FromResult<T?>(null);
    }

    /// <inheritdoc />
    public Task<List<T>> SearchAsync<T>(string query, int maxResults = 10, CancellationToken cancellationToken = default) where T : class
    {
        WarnOnce();
        // Simple keyword search on stored values
        var results = _store.Values
            .OfType<T>()
            .Take(maxResults)
            .ToList();
        return Task.FromResult(results);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        WarnOnce();
        return Task.FromResult(_store.TryRemove(key, out _));
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_store.ContainsKey(key));
    }
}
