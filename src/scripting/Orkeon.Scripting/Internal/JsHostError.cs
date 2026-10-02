using System.Globalization;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Orkeon.Domain.Autonomous;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Versioning;

namespace Orkeon.Scripting.Internal;

/// <summary>
/// The one way a CLR helper called from a JS trampoline reports a failure (SCR-25).
/// </summary>
/// <remarks>
/// <para>Under the engine's default interop policy a CLR exception thrown by a delegate does not
/// become a JavaScript exception: it unwinds straight through the script — no <c>catch</c>, no
/// <c>finally</c> — leaves the enclosing async function's promise pending forever, and erupts
/// out of whichever CLR frame happened to be draining the event loop (measured on Jint 4.16.1,
/// 2026-09-13). A trampoline whose <c>finally</c> releases a semaphore or ends a run cannot
/// live with that. Jint's <c>Interop.ExceptionHandler</c> is not the answer either: it turns
/// every exception into a bare <c>Error</c> and drops the CLR object, including one bridged
/// deliberately.</para>
/// <para>So the bridge is explicit. <see cref="Wrap"/> builds a JS <c>Error</c> that carries the
/// CLR exception on its <c>clr</c> property and returns it as a <see cref="JavaScriptException"/>
/// — thrown from a delegate, that IS a JavaScript throw: the script's <c>catch</c> and
/// <c>finally</c> run, the promise rejects with the Error, and <see cref="Unwrap"/> gives the
/// CLR side its typed exception back (<c>JsCrew.TryUnwrapTypedHostException</c>, the error
/// policy's code mapping, the cancellation rule at the root pumps).</para>
/// <para>A faulted <see cref="Task"/> awaited by the script needs none of this: Jint's task
/// bridge rejects the promise with the <see cref="AggregateException"/> as a wrapped CLR object,
/// which <see cref="Unwrap"/> also recognises.</para>
/// <para>The policy, one for every trampoline: every synchronous helper handed to JavaScript goes
/// through <see cref="Guard"/>, uniformly — the ones with no failure path of their own included,
/// so that a failure path added later is bridged by construction and a reader never has to
/// decide which helper was left raw on purpose. Only the awaited <see cref="Task"/>s stay raw —
/// except the ones a script is told to catch by type (<see cref="BridgeAsync"/>).</para>
/// <para>The Error is an instance of the class <c>errors.d.ts</c> declares for the exception —
/// <c>ReceiveTimeoutError</c> for a <see cref="ReceiveTimeoutException"/> — with that class's
/// fields set (GAP-12). The classes are planted as globals by <see cref="PlantErrorClasses"/>, so
/// <c>err instanceof ReceiveTimeoutError</c> is the idiom it reads as. They used to be declared
/// and never planted: the check compiled and threw a <c>ReferenceError</c>.</para>
/// </remarks>
internal static class JsHostError
{
    private const string ClrProperty = "clr";

    /// <summary>The error classes <c>errors.d.ts</c> declares, in its order.</summary>
    internal static readonly string[] ErrorClassNames =
    [
        "ScriptVersionMismatchError", "AgentAlreadyInCrewError", "AgentNotInCrewError",
        "AgentNotInThisCrewError", "DuplicateAgentNameError", "RecursiveAgentInvocationError",
        "WaiterKickedError", "ReceiveTimeoutError", "StateMutationOutsideWithError", "BudgetExhaustedError",
        "UnknownToolError",
    ];

    private static readonly string ErrorClassNamesJson = System.Text.Json.JsonSerializer.Serialize(ErrorClassNames);

    /// <summary>
    /// The Error factory; evaluated per engine by <see cref="JsTrampolineFactories.HostError"/>. It
    /// builds the declared classes once per engine — each an <c>Error</c> subclass whose
    /// <c>name</c> is its own — and returns <c>make</c>, carrying them on <c>make.classes</c>.
    /// </summary>
    internal static readonly string FactorySource = $$"""
        (() => {
            const classes = {};
            for (const name of {{ErrorClassNamesJson}}) {
                const C = ({ [name]: class extends Error {} })[name];
                Object.defineProperty(C.prototype, "name", { value: name, writable: true, configurable: true });
                classes[name] = C;
            }
            const make = (message, clr, clrType, className, fields) => {
                const C = (className && classes[className]) || Error;
                const err = new C(message);
                err.clr = clr;
                err.clrType = clrType;
                if (fields) Object.assign(err, fields);
                return err;
            };
            make.classes = classes;
            return make;
        })()
        """;

    /// <summary>The async bridge; evaluated per engine by <see cref="JsTrampolineFactories.HostAsync"/>.</summary>
    internal const string AsyncFactorySource = """
        (raw, convert) => async function (...args) {
            try { return await raw(...args); }
            catch (e) { throw convert(e); }
        }
        """;

    /// <summary>
    /// Plants the declared error classes as globals on <paramref name="engine"/>. Called by
    /// <see cref="JsEngineFactory.Create"/> right after the factories are prepared.
    /// </summary>
    public static void PlantErrorClasses(Engine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var classes = JsTrampolineFactories.HostError.For(engine).AsObject().Get("classes").AsObject();
        foreach (var name in ErrorClassNames)
            engine.SetValue(name, classes.Get(name));
    }

    /// <summary>
    /// The declared class an exception surfaces as, with its fields; <see langword="null"/> for an
    /// exception <c>errors.d.ts</c> declares no class for (a plain <c>Error</c> then).
    /// </summary>
    private static (string ClassName, (string Key, object Value)[] Fields)? Describe(Exception exception) => exception switch
    {
        ScriptVersionMismatchError v => ("ScriptVersionMismatchError", [("declaredVersion", v.DeclaredVersion), ("supportedVersion", v.SupportedVersion)]),
        AgentAlreadyInCrewException a => ("AgentAlreadyInCrewError", [("agentName", a.AgentName), ("crewName", a.CrewName)]),
        AgentNotInCrewException a => ("AgentNotInCrewError", [("agentName", a.AgentName)]),
        AgentNotInThisCrewException a => ("AgentNotInThisCrewError", [("agentName", a.AgentName), ("crewName", a.CrewName)]),
        DuplicateAgentNameException d => ("DuplicateAgentNameError", [("agentName", d.AgentName), ("crewName", d.CrewName)]),
        RecursiveAgentInvocationException r => ("RecursiveAgentInvocationError", [("agentName", r.AgentName)]),
        WaiterKickedException w => ("WaiterKickedError", [("queueName", w.QueueName)]),
        ReceiveTimeoutException r => ("ReceiveTimeoutError", [("timeoutMs", r.Timeout.TotalMilliseconds)]),
        StateMutationOutsideWithException s => ("StateMutationOutsideWithError", [("propertyName", s.PropertyName)]),
        BudgetExhaustedException b => ("BudgetExhaustedError", [("dimension", CamelCase(b.Dimension.ToString()))]),
        UnknownToolException u => ("UnknownToolError", [("agentName", u.AgentName), ("toolNames", u.ToolNames), ("availableTools", u.AvailableTools)]),
        _ => null,
    };

    private static string CamelCase(string name)
        => name.Length == 0 ? name : char.ToLower(name[0], CultureInfo.InvariantCulture) + name[1..];

    /// <summary>
    /// A field as the script reads it: a list of names is a real JavaScript array — what
    /// <c>errors.d.ts</c> declares, with <c>join</c> and <c>includes</c> — not a wrapped CLR list.
    /// </summary>
    private static JsValue FieldValue(Engine engine, object value) => value is IReadOnlyList<string> names
        ? new JsArray(engine, [.. names.Select(name => (JsValue)name)])
        : JsValue.FromObject(engine, value);

    /// <summary>
    /// A JavaScript throw of <paramref name="exception"/>: an <c>Error</c> with the CLR message,
    /// the CLR exception on <c>clr</c> and its type name on <c>clrType</c>. A
    /// <see cref="JavaScriptException"/> is returned as it is — it already is a JS throw.
    /// </summary>
    public static JavaScriptException Wrap(Engine engine, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is JavaScriptException already)
            return already;

        var factory = JsTrampolineFactories.HostError.For(engine);
        JsValue className = JsValue.Undefined;
        JsValue fields = JsValue.Undefined;
        if (Describe(exception) is { } described)
        {
            className = described.ClassName;
            var bag = new JsObject(engine);
            foreach (var (key, value) in described.Fields)
                bag.Set(key, FieldValue(engine, value));
            fields = bag;
        }
        var error = engine.Invoke(factory, exception.Message, exception, exception.GetType().Name, className, fields);
        return new JavaScriptException(error);
    }

    /// <summary>
    /// <paramref name="raw"/> — a CLR delegate returning a <see cref="Task"/> — as a JS async function
    /// whose rejection is the declared error class: a faulted task otherwise reaches the script as the
    /// wrapped <see cref="AggregateException"/>, never an <c>Error</c>, so <c>err instanceof
    /// ReceiveTimeoutError</c> could not hold. The conversion runs in the function's own <c>catch</c>,
    /// a promise reaction on the thread draining the engine — never on the pool thread that faulted
    /// the task. A rejection with no declared class (a cancellation) passes through unchanged.
    /// </summary>
    public static JsValue BridgeAsync(Engine engine, Delegate raw)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(raw);
        Func<JsValue, JsValue> convert = rejected => Guard(engine, () => ToDeclaredError(engine, rejected));
        return engine.Invoke(JsTrampolineFactories.HostAsync.For(engine), JsValue.FromObject(engine, raw), JsValue.FromObject(engine, convert));
    }

    /// <summary>
    /// A rejection value as the declared error class when it carries an exception that has one —
    /// a wrapped CLR exception from a faulted task becomes the bridged Error — else unchanged.
    /// Call on the engine thread.
    /// </summary>
    internal static JsValue ToDeclaredError(Engine engine, JsValue rejected)
    {
        if (rejected is JsError || Unwrap(rejected) is not { } clr)
            return rejected;
        var innermost = JsExceptionUnwrap.UnwrapToInnermost(clr);
        return Describe(innermost) is null ? rejected : Wrap(engine, innermost).Error;
    }

    /// <summary>
    /// The CLR exception <paramref name="value"/> carries, or <see langword="null"/> for a value
    /// that is not a bridged Error nor a wrapped CLR exception (a script's own <c>throw</c>).
    /// </summary>
    public static Exception? Unwrap(JsValue? value)
    {
        if (value is null || !value.IsObject())
            return null;
        if (value.ToObject() is Exception direct)
            return direct;
        var clr = value.AsObject().Get(ClrProperty);
        return clr.IsObject() ? clr.ToObject() as Exception : null;
    }

    /// <summary>
    /// Runs <paramref name="body"/> and bridges anything it throws, so a CLR helper handed to a
    /// trampoline never unwinds through the script.
    /// </summary>
    public static T Guard<T>(Engine engine, Func<T> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            return body();
        }
        catch (Exception ex) when (ex is not JavaScriptException)
        {
            throw Wrap(engine, ex);
        }
    }

    /// <inheritdoc cref="Guard{T}"/>
    public static void Guard(Engine engine, Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        try
        {
            body();
        }
        catch (Exception ex) when (ex is not JavaScriptException)
        {
            throw Wrap(engine, ex);
        }
    }
}
