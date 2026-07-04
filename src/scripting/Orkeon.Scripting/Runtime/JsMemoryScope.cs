using System.Collections.Concurrent;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Simple in-memory key/value store exposed to scripts as <c>ctx.memory.crew</c> (and,
/// in SCR-08, <c>ctx.memory.agent</c>). Search returns a tiny substring-match ranking
/// suitable for tests; real semantic search lands when the DSL is wired through to
/// <c>IMemoryProvider</c> implementations.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591
public sealed class JsMemoryScope
{
    private readonly ConcurrentDictionary<string, object?> _store = new(StringComparer.Ordinal);

    public JsMemoryScope()
    {
        search = Search;
    }

    public Func<string, object?, Task> store => (key, value) =>
    {
        _store[key] = value;
        return Task.CompletedTask;
    };

    public Func<string, Task<object?>> get => key
        => Task.FromResult(_store.TryGetValue(key, out var v) ? v : null);

    public Func<string, Task<bool>> delete => key
        => Task.FromResult(_store.TryRemove(key, out _));

    public Func<string, int, Task<IReadOnlyList<JsMemoryHit>>> search { get; }

    private Task<IReadOnlyList<JsMemoryHit>> Search(string query, int k)
    {
        var hits = _store
            .Where(kv => MatchesQuery(kv.Key, kv.Value, query))
            .Take(Math.Max(1, k <= 0 ? 5 : k))
            .Select((kv, idx) => new JsMemoryHit(kv.Key, 1.0 - (idx * 0.1), kv.Value))
            .ToList();
        return Task.FromResult<IReadOnlyList<JsMemoryHit>>(hits);
    }

    private static bool MatchesQuery(string key, object? value, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        if (key.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        var serialized = value?.ToString();
        return serialized is not null && serialized.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class JsMemoryHit
{
    public string id { get; }
    public double score { get; }
    public object? value { get; }

    internal JsMemoryHit(string id, double score, object? value)
    {
        this.id = id;
        this.score = score;
        this.value = value;
    }
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
