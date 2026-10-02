
using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Services;
using Orkeon.Application.Interfaces.Ports;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using System.Collections.Concurrent;
using DomainMemoryType = Orkeon.Domain.Memory.MemoryType;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Application.Memory;

/// <summary>
/// Basic implementation of IMemoryService for crew-based memory management.
/// </summary>
public partial class MemoryService : IMemoryService, IDisposable
{
    private readonly IMemoryProviderFactory _memoryProviderFactory;
    private readonly ILogger<MemoryService> _logger;
    private readonly CrewMemoryProviderRegistry? _providerRegistry;
    private readonly ConcurrentDictionary<CrewId, CrewMemorySystem> _memorySystems = new();

    /// <summary>
    /// Initializes a new instance of <see cref="MemoryService"/>.
    /// </summary>
    /// <param name="memoryProviderFactory">Factory handing out the shared provider of each type.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="providerRegistry">
    /// Optional per-crew provider selections (P2-O-02). When a crew has a recorded provider, its
    /// long-term memory is backed by that type's shared <see cref="IMemoryProvider"/>; otherwise the
    /// in-process default store is used.
    /// </param>
    public MemoryService(
        IMemoryProviderFactory memoryProviderFactory,
        ILogger<MemoryService> logger,
        CrewMemoryProviderRegistry? providerRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(memoryProviderFactory);
        _memoryProviderFactory = memoryProviderFactory;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _providerRegistry = providerRegistry;
    }

    /// <summary>
    /// Get Memory System.
    /// </summary>
    public ICrewMemorySystem GetMemorySystem(CrewId crewId)
    {
        return _memorySystems.GetOrAdd(crewId, ResolveMemorySystem);
    }

    private CrewMemorySystem ResolveMemorySystem(CrewId crewId)
    {
        // A crew that declared a memory provider (carried on the aggregate, recorded at kickoff) gets
        // its long-term memory backed by that type's shared provider: the crew names the type, the
        // host's section for that provider supplies the connection, and two crews of the same type
        // share one instance the factory owns — with the RAG store of that type. The crew's name,
        // recorded with the type, scopes its entries in that shared store (GAP-20). Unknown types
        // fall back to in-memory with a warning inside the factory.
        var registry = _providerRegistry;
        var providerType = registry?.GetProvider(crewId);
        if (registry is null || string.IsNullOrWhiteSpace(providerType))
            return new CrewMemorySystem(crewId, provider: null, _logger);

        var provider = _memoryProviderFactory.GetProvider(providerType);
        var scope = registry.GetScope(crewId);
        LogCrewMemoryProviderResolved(crewId, providerType, scope);
        return new CrewMemorySystem(crewId, provider, _logger, scope);
    }

    /// <summary>
    /// Save Memory Async.
    /// </summary>
    public System.Threading.Tasks.Task SaveMemoryAsync(
        CrewId crewId,
        MemoryItem item,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return SaveMemoryCoreAsync();

        async System.Threading.Tasks.Task SaveMemoryCoreAsync()
        {
            if (GetMemorySystem(crewId) is not ICrewMemorySystem memorySystem)
            {
                throw new InvalidOperationException($"Invalid memory system for crew {crewId}");
            }

            if (item.Importance > (float)SearchDefaults.DefaultSimilarityThreshold)
            {
                await memorySystem.LongTerm.AddAsync(item).ConfigureAwait(false);
            }
            else
            {
                await memorySystem.ShortTerm.AddAsync(item).ConfigureAwait(false);
            }

            LogMemorySaved(crewId);
        }
    }

    /// <summary>
    /// Search Memory Async.
    /// </summary>
    public async System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> SearchMemoryAsync(
        CrewId crewId,
        string query,
        int maxResults = 10,
        DomainMemoryType? typeFilter = null,
        CancellationToken cancellationToken = default)
    {
        if (GetMemorySystem(crewId) is not ICrewMemorySystem memorySystem)
        {
            return [];
        }

        var results = new List<MemoryItem>();

        // Search across memory types based on filter
        if (!typeFilter.HasValue || typeFilter.Value == DomainMemoryType.ShortTerm)
        {
            var shortTermResults = await memorySystem.ShortTerm.GetRecentAsync(maxResults).ConfigureAwait(false);
            results.AddRange(shortTermResults);
        }

        if (!typeFilter.HasValue || typeFilter.Value == DomainMemoryType.LongTerm)
        {
            var longTermResults = await memorySystem.LongTerm.SearchAsync(query, maxResults).ConfigureAwait(false);
            results.AddRange(longTermResults);
        }

        // Delegate relevance ranking to the Domain service
        return MemoryRelevanceService.RankByRelevance(results, maxResults);
    }

    /// <summary>
    /// Clear Memory Async.
    /// </summary>
    public async System.Threading.Tasks.Task ClearMemoryAsync(
        CrewId crewId,
        DomainMemoryType? typeFilter = null,
        CancellationToken cancellationToken = default)
    {
        if (GetMemorySystem(crewId) is not ICrewMemorySystem memorySystem)
        {
            return;
        }

        if (!typeFilter.HasValue || typeFilter.Value == DomainMemoryType.ShortTerm)
        {
            await memorySystem.ShortTerm.ClearAsync().ConfigureAwait(false);
        }

        if (!typeFilter.HasValue || typeFilter.Value == DomainMemoryType.LongTerm)
        {
            await memorySystem.LongTerm.ClearAsync().ConfigureAwait(false);
        }

        LogMemoryCleared(crewId, typeFilter);
    }

    /// <inheritdoc />
    public void ReleaseMemorySystem(CrewId crewId)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        if (!_memorySystems.TryRemove(crewId, out var system))
            return;

        (system as IDisposable)?.Dispose();
    }

    /// <summary>Disposes all memory systems and their underlying resources.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases managed resources when <paramref name="disposing"/> is <c>true</c>.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;
        foreach (var kvp in _memorySystems)
        {
            if (kvp.Value is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        _memorySystems.Clear();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Crew {CrewId} long-term memory backed by provider '{ProviderType}', scope '{Scope}'")]
    private partial void LogCrewMemoryProviderResolved(object crewId, string providerType, string scope);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Saved memory for crew {CrewId}")]
    private partial void LogMemorySaved(object crewId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleared memory for crew {CrewId} with filter {TypeFilter}")]
    private partial void LogMemoryCleared(object crewId, DomainMemoryType? typeFilter);
}

/// <summary>
/// Implementation of ICrewMemorySystem for a specific crew.
/// </summary>
internal class CrewMemorySystem : ICrewMemorySystem, IDisposable
{
    public IShortTermMemory ShortTerm { get; }
    public ILongTermMemory LongTerm { get; }
    public IEntityMemory Entities { get; }
    public IContextualMemory Contextual { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="CrewMemorySystem"/>. When <paramref name="provider"/>
    /// is supplied (crew declared a memory provider), long-term memory is durably backed by it,
    /// within <paramref name="scope"/> (the crew's name, else its id); otherwise the in-process
    /// default store is used. Short-term memory stays an in-process sliding window in both cases
    /// (ephemeral by design).
    /// </summary>
    public CrewMemorySystem(CrewId crewId, IMemoryProvider? provider, ILogger logger, string? scope = null)
    {
        ArgumentNullException.ThrowIfNull(crewId);
        ShortTerm = new SimpleShortTermMemory();
        LongTerm = provider is not null
            ? new ProviderBackedLongTermMemory(provider, string.IsNullOrWhiteSpace(scope) ? crewId.ToString() : scope)
            : new InternalLongTermMemory();
        Entities = new SimpleEntityMemory();
        Contextual = new SimpleContextualMemory(ShortTerm, LongTerm);
    }

    /// <summary>
    /// Disposes underlying memory stores that hold SemaphoreSlim instances.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;
        (ShortTerm as IDisposable)?.Dispose();
        (LongTerm as IDisposable)?.Dispose();
    }
}

/// <summary>
/// Simple in-memory short-term memory implementation.
/// </summary>
internal class SimpleShortTermMemory : IShortTermMemory
{
    // Lock-free queue: removes the mixed-use SemaphoreSlim that previously forced a
    // blocking SemaphoreSlim.Wait() inside the synchronous Clear() path (ANT-011).
    private readonly System.Collections.Concurrent.ConcurrentQueue<MemoryItem> _items = new();
    private const int MaxItems = 50;

    /// <summary>
    /// Add Async.
    /// </summary>
    public System.Threading.Tasks.Task AddAsync(MemoryItem item)
    {
        _items.Enqueue(item);
        while (_items.Count > MaxItems && _items.TryDequeue(out _))
        {
            // Intentionally empty: the drain happens in the loop condition via TryDequeue,
            // evicting oldest items until the queue is back within MaxItems.
        }

        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// Get Recent Async.
    /// </summary>
    public System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> GetRecentAsync(int count = 10)
    {
        IReadOnlyList<MemoryItem> recent = _items.ToArray().TakeLast(count).ToList();
        return System.Threading.Tasks.Task.FromResult(recent);
    }

    /// <summary>
    /// Clear Async.
    /// </summary>
    public System.Threading.Tasks.Task ClearAsync()
    {
        _items.Clear();
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// Clear.
    /// </summary>
    public void Clear()
    {
        _items.Clear();
    }
}

/// <summary>
/// Simple in-memory long-term memory implementation.
/// </summary>
internal class InternalLongTermMemory : ILongTermMemory, IDisposable
{
    private readonly List<MemoryItem> _items = [];
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Add Async.
    /// </summary>
    public async System.Threading.Tasks.Task AddAsync(MemoryItem item)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            _items.Add(item);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Search Async.
    /// </summary>
    public async System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> SearchAsync(string query, int maxResults = 10)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            // Simple keyword search
            var results = _items
                .Where(item => item.Content.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.Timestamp)
                .Take(maxResults)
                .ToList();
            return results;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Clear Async.
    /// </summary>
    public async System.Threading.Tasks.Task ClearAsync()
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            _items.Clear();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Disposes the underlying SemaphoreSlim.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing) _semaphore.Dispose();
    }
}

/// <summary>
/// Long-term memory backed by a crew-declared <see cref="IMemoryProvider"/> (Redis, SQLite, …).
/// Adapts the provider's key/value + search surface to <see cref="ILongTermMemory"/> so a crew's
/// durable memory actually lands in the selected store instead of the in-process list (P2-O-02).
/// </summary>
/// <remarks>
/// <para>
/// The provider is the factory's instance for its type, shared with every other crew of that type
/// and with the RAG store of that type (its chunks, manifests and registries). The crew's
/// <c>scope</c> — its name, else its id — keeps its entries apart (GAP-20): every entry stored
/// here carries <c>kind = crew-memory</c> and <c>crew = &lt;scope&gt;</c>, and every search asks
/// the provider for those two properties, which it applies before its limit. A crew therefore
/// reads what it stored, in this run and in the earlier runs of a crew of its name, and nothing
/// else: no other crew's memory, no RAG chunk.
/// </para>
/// <para>
/// Clearing removes only the entries this memory stored, never the whole shared store. The
/// provider is not owned here and is never disposed by this class.
/// </para>
/// </remarks>
internal sealed class ProviderBackedLongTermMemory : ILongTermMemory
{
    /// <summary>The custom property naming what an entry is.</summary>
    internal const string KindProperty = "kind";

    /// <summary>The <see cref="KindProperty"/> of a crew's long-term memory.</summary>
    internal const string CrewMemoryKind = "crew-memory";

    /// <summary>The custom property naming whose memory an entry is: the crew's scope.</summary>
    internal const string CrewProperty = "crew";

    private readonly IMemoryProvider _provider;
    private readonly string _scope;
    private readonly ConcurrentDictionary<string, byte> _storedKeys = new(StringComparer.Ordinal);

    public ProviderBackedLongTermMemory(IMemoryProvider provider, string scope)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        _provider = provider;
        _scope = scope;
    }

    public async System.Threading.Tasks.Task AddAsync(MemoryItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var entry = OwnEntry(item);
        var key = entry.Id.ToString();
        await _provider.StoreAsync(key, entry).ConfigureAwait(false);
        _storedKeys.TryAdd(key, 0);
    }

    /// <summary>
    /// The entry stored for <paramref name="item"/>: the same item — id, content, embedding,
    /// importance, metadata — with <c>kind</c> and <c>crew</c> set. A copy, so the caller's item
    /// is left as it was built.
    /// </summary>
    private MemoryItem OwnEntry(MemoryItem item)
    {
        var properties = item.Metadata.CustomProperties is { } existing
            ? new Dictionary<string, string>(existing, existing.Comparer)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        properties[KindProperty] = CrewMemoryKind;
        properties[CrewProperty] = _scope;

        return MemoryItem.Restore(
            item.Id, item.Content, item.Embedding, item.Importance, item.Metadata with { CustomProperties = properties });
    }

    public async System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> SearchAsync(string query, int maxResults = 10)
    {
        var results = await _provider.SearchAsync(query, maxResults, OwnEntries()).ConfigureAwait(false);
        return results.ToList();
    }

    /// <summary>The filter that selects this crew's entries, and nothing else, in the shared store.</summary>
    private Dictionary<string, object> OwnEntries() => new(StringComparer.Ordinal)
    {
        [KindProperty] = CrewMemoryKind,
        [CrewProperty] = _scope,
    };

    public async System.Threading.Tasks.Task ClearAsync()
    {
        foreach (var key in _storedKeys.Keys)
        {
            await _provider.DeleteAsync(key).ConfigureAwait(false);
            _storedKeys.TryRemove(key, out _);
        }
    }
}

/// <summary>
/// Simple in-memory entity memory implementation.
/// </summary>
internal class SimpleEntityMemory : IEntityMemory
{
    private readonly ConcurrentDictionary<string, MemoryEntity> _entities = new();

    /// <summary>
    /// Add Entity Async.
    /// </summary>
    public System.Threading.Tasks.Task AddEntityAsync(string entityName, EntityType type, Dictionary<string, string> attributes)
    {
        var entity = MemoryEntity.Create(entityName, type, attributes);
        _entities[entityName] = entity;
        return System.Threading.Tasks.Task.CompletedTask;
    }

    /// <summary>
    /// Get Entity Async.
    /// </summary>
    public System.Threading.Tasks.Task<MemoryEntity?> GetEntityAsync(string entityName)
    {
        _entities.TryGetValue(entityName, out var entity);
        return System.Threading.Tasks.Task.FromResult(entity);
    }

    /// <summary>
    /// Get Entities By Type Async.
    /// </summary>
    public System.Threading.Tasks.Task<IReadOnlyList<MemoryEntity>> GetEntitiesByTypeAsync(EntityType type)
    {
        var entities = _entities.Values
            .Where(e => e.Type == type)
            .ToList();
        return System.Threading.Tasks.Task.FromResult<IReadOnlyList<MemoryEntity>>(entities);
    }

    /// <summary>
    /// Update Entity Async.
    /// </summary>
    public System.Threading.Tasks.Task UpdateEntityAsync(string entityName, Dictionary<string, string> attributes)
    {
        if (_entities.TryGetValue(entityName, out var entity))
        {
            var updated = entity with
            {
                Attributes = attributes,
                LastUpdated = DateTime.UtcNow
            };
            _entities[entityName] = updated;
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }
}

/// <summary>
/// Simple contextual memory combining short and long term.
/// </summary>
internal class SimpleContextualMemory : IContextualMemory
{
    private readonly IShortTermMemory _shortTerm;
    private readonly ILongTermMemory _longTerm;

    /// <summary>
    /// Initializes a new instance of <see cref="SimpleContextualMemory"/>.
    /// </summary>
    public SimpleContextualMemory(IShortTermMemory shortTerm, ILongTermMemory longTerm)
    {
        _shortTerm = shortTerm;
        _longTerm = longTerm;
    }

    /// <summary>
    /// Get Context Async.
    /// </summary>
    public async System.Threading.Tasks.Task<string> GetContextAsync(string query, int maxTokens = 1000)
    {
        var memories = await GetRelevantMemoriesAsync(query, 20).ConfigureAwait(false);
        var context = string.Join("\n", memories.Select(m => m.Content));

        // Truncate to max tokens (rough approximation)
        if (context.Length > maxTokens * 4)
        {
            context = context.Substring(0, maxTokens * 4);
        }

        return context;
    }

    /// <summary>
    /// Get Relevant Memories Async.
    /// </summary>
    public async System.Threading.Tasks.Task<IReadOnlyList<MemoryItem>> GetRelevantMemoriesAsync(string context, int count = 20)
    {
        var shortTermMemories = await _shortTerm.GetRecentAsync(count / 2).ConfigureAwait(false);
        var longTermMemories = await _longTerm.SearchAsync(context, count / 2).ConfigureAwait(false);

        // Delegate relevance ranking to the Domain service
        return MemoryRelevanceService.RankByRelevance(
            shortTermMemories.Concat(longTermMemories), count);
    }
}
