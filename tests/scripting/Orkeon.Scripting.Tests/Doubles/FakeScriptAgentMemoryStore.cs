using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>In-memory recording fake of <see cref="IScriptAgentMemoryStore"/>.</summary>
public sealed class FakeScriptAgentMemoryStore : IScriptAgentMemoryStore
{
    /// <summary>Entries keyed by (agent key, entry key), values as JSON text.</summary>
    public readonly ConcurrentDictionary<(string Agent, string Key), string> Entries = new();

    /// <summary>Every agent key a call carried.</summary>
    public readonly ConcurrentBag<string> AgentKeysSeen = new();

    public Task StoreAsync(string agentKey, string key, string valueJson, CancellationToken ct)
    {
        AgentKeysSeen.Add(agentKey);
        Entries[(agentKey, key)] = valueJson;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string agentKey, string key, CancellationToken ct)
    {
        AgentKeysSeen.Add(agentKey);
        return Task.FromResult(Entries.TryGetValue((agentKey, key), out var v) ? v : null);
    }

    public Task<IReadOnlyList<ScriptAgentMemoryHit>> SearchAsync(string agentKey, string query, int limit, CancellationToken ct)
    {
        AgentKeysSeen.Add(agentKey);
        IReadOnlyList<ScriptAgentMemoryHit> hits = Entries
            .Where(e => e.Key.Agent == agentKey
                && (e.Key.Key.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Value.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Take(limit <= 0 ? 5 : limit)
            .Select(e => new ScriptAgentMemoryHit { Key = e.Key.Key, ValueJson = e.Value })
            .ToList();
        return Task.FromResult(hits);
    }

    public Task<bool> DeleteAsync(string agentKey, string key, CancellationToken ct)
    {
        AgentKeysSeen.Add(agentKey);
        return Task.FromResult(Entries.TryRemove((agentKey, key), out _));
    }
}
