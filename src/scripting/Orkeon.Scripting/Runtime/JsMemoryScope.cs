using System.Collections.Concurrent;
using System.Text.Json;
using Orkeon.Application.Interfaces.Ports;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Key/value store exposed to scripts as <c>ctx.memory.crew</c> and <c>ctx.memory.agent</c>. In memory
/// by default (search is a tiny substring-match ranking); when built over an
/// <see cref="IScriptAgentMemoryStore"/> every call goes to that durable store for one agent key (RT-16).
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of MemoryScope/MemoryHit in Typings/context.d.ts; that declaration is the contract scripts read.
public sealed class JsMemoryScope
{
    private readonly ConcurrentDictionary<string, object?> _store = new(StringComparer.Ordinal);
    private readonly IScriptAgentMemoryStore? _durable;
    private readonly string _agentKey = "";
    private readonly CancellationToken _ct;

    public JsMemoryScope()
    {
        search = Search;
    }

    internal JsMemoryScope(IScriptAgentMemoryStore durable, string agentKey, CancellationToken ct)
        : this()
    {
        _durable = durable;
        _agentKey = agentKey;
        _ct = ct;
    }

    public Func<string, object?, Task> store => (key, value) =>
    {
        if (_durable is not null) return _durable.StoreAsync(_agentKey, key, JsonSerializer.Serialize(value), _ct);
        _store[key] = value;
        return Task.CompletedTask;
    };

    public Func<string, Task<object?>> get => async key =>
    {
        if (_durable is null) return _store.TryGetValue(key, out var v) ? v : null;
        var json = await _durable.GetAsync(_agentKey, key, _ct).ConfigureAwait(false);
        return json is null ? null : FromJson(json);
    };

    public Func<string, Task<bool>> delete => key
        => _durable is not null
            ? _durable.DeleteAsync(_agentKey, key, _ct)
            : Task.FromResult(_store.TryRemove(key, out _));

    public Func<string, int, Task<IReadOnlyList<JsMemoryHit>>> search { get; }

    private async Task<IReadOnlyList<JsMemoryHit>> Search(string query, int k)
    {
        var limit = Math.Max(1, k <= 0 ? 5 : k);
        if (_durable is not null)
        {
            var found = await _durable.SearchAsync(_agentKey, query, limit, _ct).ConfigureAwait(false);
            return found.Select((h, idx) => new JsMemoryHit(h.Key, 1.0 - (idx * 0.1), FromJson(h.ValueJson))).ToList();
        }

        return _store
            .Where(kv => MatchesQuery(kv.Key, kv.Value, query))
            .Take(limit)
            .Select((kv, idx) => new JsMemoryHit(kv.Key, 1.0 - (idx * 0.1), kv.Value))
            .ToList();
    }

    private static object? FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ToClr(doc.RootElement);
    }

    private static object? ToClr(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => ToClr(p.Value), StringComparer.Ordinal),
        JsonValueKind.Array => e.EnumerateArray().Select(ToClr).ToArray(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

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
