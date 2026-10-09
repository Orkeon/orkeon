using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// Surface exposed to JS as <c>ctx</c> in agent bodies, hooks, and task callbacks.
/// SCR-07 covers everything but the <c>llm</c> namespace (SCR-09) and detailed events
/// (SCR-13); both are accessible as placeholders here so that scripts can reference
/// them without runtime errors at the API surface.
/// </summary>
#pragma warning disable IDE1006
#pragma warning disable CS1591 // JS-interop mirror of ExecutionContext/MemoryScope/Logger in Typings/context.d.ts; that declaration is the contract scripts read.
public class JsExecutionContext
{
    private readonly Engine _engine;
    private readonly JsAgent _self;
    private readonly JsCrew _crew;
    private readonly JsAgentChannel _channel;
    private readonly CancellationToken _ct;

    public JsCrewProxy crew { get; }
    public JsMemoryRoot memory { get; }
    public JsLogger log { get; }
    public JsEventBroker events { get; }
    public CancellationToken signal => _ct;
    public JsLlmFacade llm { get; }

    internal JsExecutionContext(JsExecutionEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _engine = environment.Engine;
        _self = environment.Self;
        _crew = environment.Crew;
        _channel = environment.Channel;
        _ct = environment.Ct;
        memory = new JsMemoryRoot(environment.CrewMemory);
        crew = new JsCrewProxy(environment.Crew, environment.Engine, environment.Ct);
        log = new JsLogger(environment.Logger);
        events = environment.Crew.EventBroker;
        var (tools, unknownTools) = ResolveAgentTools(environment);
        llm = new JsLlmFacade(
            environment.Engine, environment.LlmProvider, environment.Ct,
            tools,
            new JsLlmActGuards
            {
                Budget = environment.Budget,
                PermissionGate = environment.PermissionGate,
                UnknownTools = unknownTools,
            },
            new JsLlmObservability
            {
                DeltaSink = environment.DeltaSink,
                ToolInvocation = environment.ToolInvocation,
                Logger = environment.Logger,
                CrewName = environment.Crew.name,
                AgentName = environment.Self.name,
            });
    }

    /// <summary>
    /// Convenience constructor used by host integrations and tests that supply the runtime
    /// collaborators positionally without an explicit LLM provider.
    /// </summary>
    internal JsExecutionContext(
        Engine engine,
        JsAgent self,
        JsCrew crew,
        JsAgentChannel channel,
        JsMemoryScope crewMemory,
        ILogger logger,
        CancellationToken ct)
        : this(new JsExecutionEnvironment
        {
            Engine = engine,
            Self = self,
            Crew = crew,
            Channel = channel,
            CrewMemory = crewMemory,
            Logger = logger,
            Ct = ct,
        })
    {
    }

    /// <summary>
    /// Resolves the tools this agent may use in <c>ctx.llm.act</c>: the built-in tools selected
    /// via <c>agentBuilder().tools([...])</c>, matched by name case-insensitively, then the
    /// <c>toolBuilder()</c> instances of <c>withAutonomousTool(s)</c> — the same two sets the
    /// declarative shape offers (GAP-12: act used to see the first only). Empty when the agent
    /// declares neither, and act then behaves like a tool-less completion loop.
    /// </summary>
    /// <returns>
    /// The tools, and the names of <c>.tools([...])</c> the host's catalogue does not answer to —
    /// null when there are none. <c>act</c> refuses to run on those (GAP-27): a misspelled name used
    /// to be dropped without a word, where the declarative shape fails its load on it.
    /// </returns>
    private static (Orkeon.Domain.Tools.IBaseTool[] Tools, UnknownAgentTools? Unknown) ResolveAgentTools(
        JsExecutionEnvironment environment)
    {
        var selected = environment.Self.Builder.BuiltInToolNames;
        var catalogue = environment.BuiltInTools ?? [];
        var builtIns = selected.Count > 0
            ? catalogue.Where(t => selected.Contains(t.Name, System.StringComparer.OrdinalIgnoreCase))
            : [];
        var tools = builtIns.Concat(environment.Self.Builder.AutonomousTools).ToArray();

        var unknown = selected
            .Where(name => !catalogue.Any(t => string.Equals(t.Name, name, System.StringComparison.OrdinalIgnoreCase)))
            .Distinct(System.StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (unknown.Count == 0)
            return (tools, null);

        var available = catalogue.Select(t => t.Name)
            .Distinct(System.StringComparer.Ordinal)
            .Order(System.StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (tools, new UnknownAgentTools(unknown, available));
    }

    // `ctx.delegate(agentOrName, input)` IS `crew.runAgent(agentOrName, input)` (GAP-12): the
    // target runs under its instance semaphore, with its own AgentContext — its state, its
    // memory, its log — and its own onError policy. It used to invoke the body directly with
    // `undefined` as its context, so a delegated body that read `ctx` threw, and its onError
    // never ran. runAgent resolves a name the way crew.d.ts promises and refuses the agent
    // whose body is calling (RecursiveAgentInvocationException) instead of deadlocking on the
    // semaphore that body holds.
    //
    // What follows still holds: the promise runAgent returns goes straight back to JS, so the
    // caller's `await ctx.delegate(...)` settles it under the SAME single pump that already
    // drives the run, never a nested blocking pump.
    //
    // Why this matters: the Jint engine is single-threaded and non-reentrant.
    // The previous implementation awaited the body via
    // `Task.Run(() => UnwrapIfPromise(result, 30min))` — a second blocking pump
    // on a background thread. One such delegation worked, but N concurrent ones
    // (a `Promise.all` fan-out) put N background threads pumping the same engine
    // at once → deadlock. Round-28 reproduced it exactly: every delegated writer
    // burned the full 30-min ceiling ("Timeout of 00:30:00 reached") and the
    // crew promise rejected after writing only a handful of files. Handing the
    // raw promise back to JS removes the nested pump entirely: N delegated bodies
    // settle on the one engine thread under the pump that drives the run, with no
    // reentrancy and no artificial settle ceiling. This makes concurrent delegation safe.
    //
    // The signature is synchronous (returns JsValue, not Task<JsValue>): the
    // guards throw synchronously at the call site — bridged (JsHostError), so a
    // body past its first await, inside an event-loop job, sees them as a
    // JavaScript throw its catch and finally run for — and a JS `await` on the
    // returned promise does the waiting. A delegated body that rejects surfaces
    // through the pump as a normal promise rejection.
    public Func<JsValue, JsValue, JsValue> @delegate => (target, input) => JsHostError.Guard(_engine, () =>
    {
        EnsureSelfInCrew();
        _ct.ThrowIfCancellationRequested();
        var agent = ResolveMember(target, "ctx.delegate(agent, input)");
        // Linked to this context's token: cancelling the delegating body cancels the delegated one.
        var options = new Jint.Native.JsObject(_engine);
        options.Set("signal", JsValue.FromObject(_engine, _ct));
        return _engine.Invoke(_crew.runAgent, [JsValue.FromObject(_engine, agent), input ?? JsValue.Undefined, options]);
    });

    public Action<JsValue, JsValue> send => (target, message) => JsHostError.Guard(_engine, () =>
    {
        EnsureSelfInCrew();
        _channel.Send(ResolveMember(target, "ctx.send(agent, message)").name, message);
    });

    /// <summary>An <see cref="JsAgent"/> or an agent name, either way a member of this crew.</summary>
    private JsAgent ResolveMember(JsValue? target, string where)
    {
        if (target is not null && target.IsString())
            return _crew.Resolve(target.AsString());
        if (target?.ToObject() is not JsAgent agent)
            throw new InvalidScriptException($"{where} expects an Agent or an agent name.");
        if (!_crew.has(agent))
            throw new AgentNotInThisCrewException(agent.name, _crew.name);
        return agent;
    }

    private JsValue? _receiveJs;

    /// <summary>
    /// JS <c>async (options?) =&gt; message</c>: an expired <c>{ timeout }</c> rejects with a
    /// <c>ReceiveTimeoutError</c> (<see cref="JsHostError.BridgeAsync"/>).
    /// </summary>
    public JsValue receive => _receiveJs ??= JsHostError.BridgeAsync(_engine, ReceiveAsync);

    private Func<JsValue?, Task<object?>> ReceiveAsync => async options =>
    {
        EnsureSelfInCrew();
        TimeSpan? timeout = null;
        if (options is not null && options.IsObject())
        {
            var to = options.Get("timeout");
            if (to.IsNumber()) timeout = TimeSpan.FromMilliseconds(to.AsNumber());
            else if (to.IsString()) timeout = ParseDuration(to.AsString());
        }
        // Resolved as-is: a JsValue passes through Jint's bridge untouched, and a CLR
        // payload is converted on the engine's event loop rather than on this
        // thread-pool continuation.
        return await _channel.ReceiveAsync(_self.name, timeout, _ct).ConfigureAwait(false);
    };

    public Action<JsValue> broadcast => message => JsHostError.Guard(_engine, () =>
    {
        EnsureSelfInCrew();
        _channel.Broadcast(_self.name, message, _crew.agents.Select(a => a.name));
    });

    private void EnsureSelfInCrew()
    {
        if (_self.CurrentCrew is null)
            throw new AgentNotInCrewException(_self.name);
    }

    private static TimeSpan ParseDuration(string text)
    {
        // Mirrors CrewRunOptions.ParseDuration. Inlined to avoid exposing the helper.
        text = text.Trim();
        int i = 0;
        while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || text[i] == ',')) i++;
        if (!double.TryParse(text[..i], System.Globalization.CultureInfo.InvariantCulture, out var value))
            return TimeSpan.Zero;
#pragma warning disable CA1308 // normalized unit key driving the switch below; lowercase is the required form, not a comparison normalization
        var unit = text[i..].Trim().ToLowerInvariant();
#pragma warning restore CA1308
        return unit switch
        {
            "ms" => TimeSpan.FromMilliseconds(value),
            "s" => TimeSpan.FromSeconds(value),
            "m" => TimeSpan.FromMinutes(value),
            "h" => TimeSpan.FromHours(value),
            _ => TimeSpan.FromMilliseconds(value),
        };
    }
}

public sealed class JsMemoryRoot
{
    public JsMemoryScope crew { get; }
    public JsMemoryScope agent { get; internal set; } // Set per-agent in SCR-08

    internal JsMemoryRoot(JsMemoryScope crew)
    {
        this.crew = crew;
        agent = new JsMemoryScope();
    }
}

public sealed class JsCrewProxy
{
    private readonly JsCrew _crew;
    private readonly Engine _engineRef;
    private readonly CancellationToken _ct;
    private JsValue? _lockJs;

    internal JsCrewProxy(JsCrew crew, Engine engine, CancellationToken ct) { _crew = crew; _engineRef = engine; _ct = ct; }
    public JsAgent? findByName(string name) => _crew.findByName(name);
    public JsAgent? findById(string id) => _crew.findById(id);
    public IReadOnlyList<JsAgent> findByRole(string role) => _crew.findByRole(role);
    public bool has(JsAgent agent) => _crew.has(agent);
    public bool has(string agentName) => _crew.has(agentName);
    public string name => _crew.name;

    /// <summary>JS <c>async (name, fn) =&gt; result</c>: the shared named-lock trampoline (<see cref="JsTrampolineFactories.Lock"/>) over the crew's lock table, acquired with the context's token — a waiter is released when its run is cancelled, as with <c>ctx.lock</c> and a published event's <c>lock</c>.</summary>
    public JsValue @lock => _lockJs ??= BuildLockFunction();

    private JsValue BuildLockFunction()
    {
        Func<string, Task<JsValue>> acquire = async name =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var sem = _crew.GetCrewLock(name);
            await sem.WaitAsync(_ct).ConfigureAwait(false);
            return JsValue.Undefined;
        };
        Action<string> release = name => JsHostError.Guard(_engineRef, () => _crew.GetCrewLock(name).Release());
        return _engineRef.Invoke(JsTrampolineFactories.Lock.For(_engineRef), [acquire, release]);
    }
}

public sealed partial class JsLogger
{
    private readonly ILogger _logger;
    internal JsLogger(ILogger logger) { _logger = logger ?? NullLogger.Instance; }

    // `ctx.log.info("a", 1, { b: 2 })` writes `a 1 {"b":2}`, the way console.log joins its
    // arguments. The extra arguments used to fall on the floor: one parameter, the rest dropped.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1873", Justification = "Format runs only behind the IsEnabled guard on the same line; the analyzer does not see through it.")]
    public void debug(string message, params JsValue[] args) { if (_logger.IsEnabled(LogLevel.Debug)) LogDebugMessage(Format(message, args)); }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1873", Justification = "Format runs only behind the IsEnabled guard on the same line; the analyzer does not see through it.")]
    public void info(string message, params JsValue[] args) { if (_logger.IsEnabled(LogLevel.Information)) LogInfoMessage(Format(message, args)); }
    public void warn(string message, params JsValue[] args) { if (_logger.IsEnabled(LogLevel.Warning)) LogWarnMessage(Format(message, args)); }
    public void error(string message, params JsValue[] args) { if (_logger.IsEnabled(LogLevel.Error)) LogErrorMessage(Format(message, args)); }

    private static string Format(string message, JsValue[]? args)
    {
        if (args is null || args.Length == 0) return message;
        var parts = new string[args.Length + 1];
        parts[0] = message;
        for (var i = 0; i < args.Length; i++)
            parts[i + 1] = Render(args[i]);
        return string.Join(' ', parts);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "A log argument that cannot be serialised (a cycle, a host object) falls back to its JavaScript string form; logging must never throw into the script.")]
    private static string Render(JsValue value)
    {
        if (value is null || value.IsUndefined()) return "undefined";
        if (value.IsString()) return value.AsString();
        if (!value.IsObject() || value is Jint.Native.Function.Function)
            return value.ToString();
        if (value is JsError)
            return value.AsObject().Get("message").ToString();
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(value.ToObject());
        }
        catch (Exception)
        {
            return value.ToString();
        }
    }

    // --- source-generated logging ---

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "{Msg}")]
    private partial void LogDebugMessage(string msg);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "{Msg}")]
    private partial void LogInfoMessage(string msg);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "{Msg}")]
    private partial void LogWarnMessage(string msg);

    [LoggerMessage(EventId = 4, Level = LogLevel.Error, Message = "{Msg}")]
    private partial void LogErrorMessage(string msg);
}

#pragma warning restore CS1591
#pragma warning restore IDE1006
