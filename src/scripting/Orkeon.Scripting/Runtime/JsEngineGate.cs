using System.Runtime.CompilerServices;
using Jint;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// One gate per Jint engine. The engine is single-threaded and non-reentrant, but the
/// orchestration pipeline is not: a <c>process: parallel</c> crew can call two script
/// tools at the same instant, and both would race into the same engine. Every
/// <see cref="JsTool.CallAsync"/> acquires the engine's gate first, so concurrent tool
/// calls serialize on the engine while staying concurrent everywhere else.
/// <para>
/// A <see cref="ConditionalWeakTable{TKey,TValue}"/> keyed on the engine: the gate lives
/// exactly as long as the engine does, with no registry to clean up.
/// </para>
/// </summary>
internal static class JsEngineGate
{
    private static readonly ConditionalWeakTable<Engine, SemaphoreSlim> Gates = new();

    public static SemaphoreSlim For(Engine engine) =>
        Gates.GetValue(engine, static _ => new SemaphoreSlim(1, 1));
}
