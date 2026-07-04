using Microsoft.Extensions.Logging;
using Orkeon.Domain.Common;
using Orkeon.Domain.Memory;
using System.Collections.Concurrent;
using Orkeon.Domain.Constants.Memory;

namespace Orkeon.Infrastructure.Stubs;

/// <summary>
/// In-memory stub implementation of <see cref="IMemorySystem"/>.
/// Logs a warning on first use to indicate no persistent memory system is configured.
/// </summary>
public sealed partial class InMemoryMemorySystem : IMemorySystem
{
    private readonly ConcurrentDictionary<string, AgentMemoryStore> _stores = new();
    private readonly ConcurrentDictionary<string, List<MemoryItem>> _memories = new();
    private readonly ILogger<InMemoryMemorySystem> _logger;
    private int _warnedOnce;

    /// <summary>Initializes a new instance of <see cref="InMemoryMemorySystem"/>.</summary>
    public InMemoryMemorySystem(ILogger<InMemoryMemorySystem> logger)
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
        Message = "Using InMemoryMemorySystem — agent memories are not persisted. Register a persistent IMemorySystem for production.")]
    private partial void LogInMemoryFallback();

    /// <inheritdoc />
    public Task<AgentMemoryStore?> GetAgentMemoryStoreAsync(AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        WarnOnce();
        _stores.TryGetValue(agentId.Value.ToString(), out var store);
        return Task.FromResult(store);
    }

    /// <inheritdoc />
    public Task<AgentMemoryStore> CreateAgentMemoryStoreAsync(AgentId agentId)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        WarnOnce();
        var store = _stores.GetOrAdd(
            agentId.Value.ToString(),
            _ => AgentMemoryStore.Create(agentId));
        return Task.FromResult(store);
    }

    /// <inheritdoc />
    public Task StoreMemoryAsync(AgentId agentId, MemoryItem memory)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        WarnOnce();
        var key = agentId.Value.ToString();
        _memories.AddOrUpdate(
            key,
            _ => [memory],
            (_, list) => { list.Add(memory); return list; });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IEnumerable<MemoryItem>> RetrieveMemoriesAsync(AgentId agentId, string query, int limit = MemoryDefaults.DefaultSearchLimit)
    {
        ArgumentNullException.ThrowIfNull(agentId);
        WarnOnce();
        var key = agentId.Value.ToString();
        if (_memories.TryGetValue(key, out var memories))
        {
            IEnumerable<MemoryItem> result = memories.TakeLast(limit);
            return Task.FromResult(result);
        }
        return Task.FromResult(Enumerable.Empty<MemoryItem>());
    }
}
