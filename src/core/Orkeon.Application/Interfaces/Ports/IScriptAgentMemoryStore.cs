namespace Orkeon.Application.Interfaces.Ports;

/// <summary>
/// Durable per-agent memory behind <c>ctx.memory.agent</c>: entries are keyed by an agent key
/// (<c>agent:&lt;project&gt;:&lt;agent name&gt;</c>) and an entry key; values travel as JSON text.
/// </summary>
public interface IScriptAgentMemoryStore
{
    /// <summary>Stores (or replaces) the JSON value of <paramref name="key"/> for the agent.</summary>
    System.Threading.Tasks.Task StoreAsync(string agentKey, string key, string valueJson, CancellationToken ct);

    /// <summary>Returns the JSON value of <paramref name="key"/> for the agent, or null.</summary>
    Task<string?> GetAsync(string agentKey, string key, CancellationToken ct);

    /// <summary>Returns at most <paramref name="limit"/> entries whose key or value matches <paramref name="query"/>.</summary>
    Task<IReadOnlyList<ScriptAgentMemoryHit>> SearchAsync(string agentKey, string query, int limit, CancellationToken ct);

    /// <summary>Deletes an entry; returns whether it existed.</summary>
    Task<bool> DeleteAsync(string agentKey, string key, CancellationToken ct);
}

/// <summary>One entry returned by <see cref="IScriptAgentMemoryStore.SearchAsync"/>.</summary>
public sealed record ScriptAgentMemoryHit
{
    /// <summary>Entry key.</summary>
    public required string Key { get; init; }

    /// <summary>Entry value as JSON text.</summary>
    public required string ValueJson { get; init; }
}
