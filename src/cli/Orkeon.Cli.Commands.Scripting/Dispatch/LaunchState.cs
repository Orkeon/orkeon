using System.Text;
using Jint;
using Jint.Native;
using Jint.Native.Json;
using Jint.Native.Object;
using Orkeon.Scripting.Internal;

namespace Orkeon.Cli.Commands.Scripting.Dispatch;

/// <summary>
/// The <c>state</c> launch option of <c>runCrewAsync</c> and <c>commands.post</c> (#493): a JSON value the launch
/// attaches to its instance and the instance's <c>completed</c> receives back as <c>result.state</c>. It is kept
/// serialised, in memory with the instance, and parsed again for each completion, so a <c>completed</c> that
/// changes it changes nothing the launcher holds.
/// </summary>
internal static class LaunchState
{
    /// <summary>The cap of a state, serialised as UTF-8 JSON.</summary>
    internal const int MaxBytes = 16 * 1024;

    /// <summary>
    /// Serialises <c>options.state</c>, or returns <see langword="null"/> when <paramref name="options"/> carries none.
    /// A value JSON cannot hold, or one above <see cref="MaxBytes"/>, is refused as a script error naming
    /// <paramref name="caller"/>; an engine stop or a cancellation raised while serialising escapes unchanged.
    /// </summary>
    internal static string? Read(JsValue? options, string caller)
    {
        if (options is null || !options.IsObject())
            return null;
        var state = options.AsObject().Get("state");
        if (state.IsUndefined())
            return null;

        var json = Serialize(state, caller);
        var bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > MaxBytes)
            throw Refuse($"{caller}: options.state is {bytes} bytes once serialised, above the cap of {MaxBytes} bytes (16 KiB).");
        return json;
    }

    /// <summary>The state handed to a <c>completed</c>: a fresh parse of <paramref name="json"/>, or undefined without one.</summary>
    internal static JsValue Restore(Engine engine, string? json)
        => json is null ? JsValue.Undefined : new JsonParser(engine).Parse(json);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Any serialisation failure of an arbitrary script value (a cycle, a BigInt, a host object) is the same refusal; an engine stop escapes.")]
    private static string Serialize(JsValue state, string caller)
    {
        JsValue serialised;
        try
        {
            serialised = state is ObjectInstance obj
                ? new JsonSerializer(obj.Engine!).Serialize(state)
                : new JsonSerializer(s_primitives.Value!).Serialize(state);
        }
        catch (Exception ex) when (!JsHostError.IsEngineStop(ex) && ex is not OperationCanceledException)
        {
            // An engine stop (memory, statements, time, recursion, cancellation) inside a toJSON or a getter is the
            // script being stopped, not a value JSON cannot hold: it escapes as it does everywhere else.
            throw Refuse($"{caller}: options.state must be a JSON-serialisable value ({ex.Message}).");
        }
        if (!serialised.IsString())
            throw Refuse($"{caller}: options.state must be a JSON-serialisable value, not '{state}'.");
        return serialised.AsString();
    }

    // A primitive serialises without touching an engine's realm: one engine per thread is enough.
    private static readonly ThreadLocal<Engine> s_primitives = new(() => new Engine());

    private static Jint.Runtime.JavaScriptException Refuse(string message) => new(new JsString(message));
}
