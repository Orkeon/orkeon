using System.Runtime.CompilerServices;
using Jint;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// The open attempts of every crew on an engine, in opening order — the engine-wide counterpart of a
/// crew's own attribution (<see cref="JsCrew.BeginBrokerScope"/>), kept for one reader: a run opened
/// from a body. Its parent is the innermost attempt open on the engine, whichever crew's, so a sub-crew
/// run from crew X's body is cancelled with X's run — the sub-crew has no open attempt of its own to
/// read, and a per-crew reading left such a run unlinked, its semaphore held for good once X was
/// cancelled under a body that never settles. The entries form a set in opening order, not a stack:
/// interleaved runs close their attempts in any order. Best-effort under interleaving — the innermost
/// open attempt is the most recently opened one still open, the caller under sequential nesting and a
/// neighbour under a fan-out; <c>{ signal: ctx.signal }</c> is the explicit form.
/// </summary>
/// <remarks>
/// A <see cref="ConditionalWeakTable{TKey,TValue}"/> keyed on the engine, like <see cref="JsEngineGate"/>:
/// the registry lives exactly as long as the engine, with nothing to clean up. Entries open on the
/// draining thread and close there or, when a host abandons its run, on the host thread.
/// </remarks>
internal static class JsEngineAttempts
{
    private static readonly ConditionalWeakTable<Engine, Registry> Registries = new();

    internal static void Open(Engine engine, BrokerAttribution attempt)
    {
        var registry = Registries.GetValue(engine, static _ => new Registry());
        lock (registry.Sync) registry.Entries.Add(attempt);
    }

    internal static void Close(Engine engine, BrokerAttribution attempt)
    {
        if (!Registries.TryGetValue(engine, out var registry)) return;
        lock (registry.Sync) registry.Entries.Remove(attempt);
    }

    /// <summary>The most recently opened attempt still open on <paramref name="engine"/>, or none.</summary>
    internal static BrokerAttribution? Innermost(Engine engine)
    {
        if (!Registries.TryGetValue(engine, out var registry)) return null;
        lock (registry.Sync) return registry.Entries.Count == 0 ? null : registry.Entries[^1];
    }

    private sealed class Registry
    {
        internal readonly Lock Sync = new();
        internal readonly List<BrokerAttribution> Entries = new();
    }
}
