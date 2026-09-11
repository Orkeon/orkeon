using Jint;
using Jint.Native;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// Reads a member of the CLR object an async facade surface resolves with, the way the
/// script sees it once Jint has converted it on its event loop.
/// </summary>
/// <remarks>
/// The facades resolve plain CLR objects rather than <see cref="JsValue"/>s on purpose:
/// their continuations run on thread-pool threads, and building a <see cref="JsValue"/>
/// there enters the single-threaded engine (the seven-stream race of 2026-09-11). A C#
/// test is on its own thread and owns its engine, so converting here is safe — one
/// throwaway engine per read, never a shared one, or the tests would reintroduce the
/// very race the surfaces avoid.
/// </remarks>
internal static class ClrResultExtensions
{
    public static JsValue Get(this object? result, string name)
    {
        Assert.NotNull(result);
        using var engine = new Engine();
        return JsValue.FromObject(engine, result).Get(name);
    }
}
