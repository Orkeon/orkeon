using System.Runtime.CompilerServices;
using Jint;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// One gate per Jint engine, held by every root pump for its whole drain. The engine is
/// single-threaded and non-reentrant, but its CLR callers are not: a <c>process: parallel</c>
/// crew can call two script tools at the same instant, a host can start two
/// <see cref="JsCrew.RunAsync"/> on one crew, and two synchronous drains on one engine corrupt
/// it. Every root pump — <c>ScriptHost</c>'s evaluation, <see cref="JsCrew.RunAsync"/>,
/// <see cref="JsTool.CallAsync"/> — acquires the engine's gate first, so CLR-driven work
/// serializes on the engine while staying concurrent everywhere else.
/// <para>
/// Invariant: nothing reached from under a root pump may take the gate. A CLR delegate the
/// script invokes runs inside the pump's drain, on the draining thread; <see cref="MarkDraining"/>
/// records that thread so <see cref="IsDrainingOnThisThread"/> lets a root pump refuse to start
/// from there instead of deadlocking on the gate it already holds. Re-entrancy from another
/// thread is not detectable — it is the documented rule: call <c>crew.run()</c> from the script.
/// </para>
/// <para>
/// Two <see cref="ConditionalWeakTable{TKey,TValue}"/>s keyed on the engine: the gate and the
/// drain marker live exactly as long as the engine does, with no registry to clean up.
/// </para>
/// </summary>
internal static class JsEngineGate
{
    private static readonly ConditionalWeakTable<Engine, SemaphoreSlim> Gates = new();
    private static readonly ConditionalWeakTable<Engine, DrainMarker> Markers = new();

    public static SemaphoreSlim For(Engine engine) =>
        Gates.GetValue(engine, static _ => new SemaphoreSlim(1, 1));

    /// <summary>Whether the calling thread is the engine's current root drainer.</summary>
    public static bool IsDrainingOnThisThread(Engine engine) =>
        Markers.TryGetValue(engine, out var marker) && marker.ThreadId == Environment.CurrentManagedThreadId;

    /// <summary>
    /// Records the calling thread as the engine's root drainer until the returned handle is
    /// disposed, which restores the previous value (a nested mark is tolerated, not expected).
    /// </summary>
    public static IDisposable MarkDraining(Engine engine)
    {
        var marker = Markers.GetValue(engine, static _ => new DrainMarker());
        var previous = marker.ThreadId;
        marker.ThreadId = Environment.CurrentManagedThreadId;
        return new DrainScope(marker, previous);
    }

    private sealed class DrainMarker
    {
        public volatile int ThreadId = -1;
    }

    private sealed class DrainScope(DrainMarker marker, int previous) : IDisposable
    {
        public void Dispose() => marker.ThreadId = previous;
    }
}
