using Jint;
using Jint.Native;

namespace Orkeon.Scripting.Tests.Testing;

/// <summary>
/// CLR helpers planted as globals on a test engine, shared by the tests that drive a crew from
/// the CLR: a Task the script awaits, a log it appends to, a cancellation it raises itself.
/// </summary>
internal static class ScriptGlobals
{
    /// <summary>Installs <c>__sleep(ms[, ct])</c>: a Task the script awaits, cancelled by the token it was given (<c>ctx.signal</c>), or by nothing.</summary>
    public static void InstallSleep(Engine engine)
    {
        engine.SetValue("__sleep", new Func<double, JsValue?, Task<JsValue>>(async (ms, ctVal) =>
        {
            var ct = CancellationToken.None;
            if (ctVal is not null && !ctVal.IsUndefined() && !ctVal.IsNull() && ctVal.ToObject() is CancellationToken token)
                ct = token;
            await Task.Delay(TimeSpan.FromMilliseconds(ms), ct).ConfigureAwait(false);
            return JsValue.Undefined;
        }));
    }

    /// <summary>Installs <c>__log(text)</c>, appending to the returned list.</summary>
    public static List<string> InstallLog(Engine engine)
    {
        var log = new List<string>();
        engine.SetValue("__log", new Action<string>(text => { lock (log) log.Add(text); }));
        return log;
    }

    /// <summary>
    /// Installs <c>__cancel()</c>, cancelling the returned source from inside the script: no timer
    /// sits between the cancel and the run's own unwind, so nothing measures the thread pool.
    /// </summary>
    public static CancellationTokenSource InstallCancel(Engine engine)
    {
        var cts = new CancellationTokenSource();
        engine.SetValue("__cancel", new Action(cts.Cancel));
        return cts;
    }
}
