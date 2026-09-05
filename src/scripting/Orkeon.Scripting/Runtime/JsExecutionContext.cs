using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.Exceptions;

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
        crew = new JsCrewProxy(environment.Crew, environment.Engine);
        log = new JsLogger(environment.Logger);
        events = environment.Crew.EventBroker;
        llm = new JsLlmFacade(
            environment.Engine, environment.LlmProvider, environment.Ct,
            ResolveAgentTools(environment), environment.Budget, environment.PermissionGate,
            new JsLlmObservability
            {
                DeltaSink = environment.DeltaSink,
                Logger = environment.Logger,
                UsageSink = environment.UsageSink,
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
    /// Resolves the IBaseTool instances this agent may use in <c>ctx.llm.act</c>: the built-in
    /// tools selected via <c>agentBuilder().tools([...])</c>, matched by name. Empty when the
    /// agent declares no built-in tools (act then behaves like a tool-less completion loop).
    /// </summary>
    private static Orkeon.Domain.Tools.IBaseTool[] ResolveAgentTools(JsExecutionEnvironment environment)
    {
        var all = environment.BuiltInTools;
        if (all is null || all.Count == 0) return System.Array.Empty<Orkeon.Domain.Tools.IBaseTool>();
        var selected = environment.Self.Builder.BuiltInToolNames;
        if (selected is null || selected.Count == 0) return System.Array.Empty<Orkeon.Domain.Tools.IBaseTool>();
        var wanted = new HashSet<string>(selected, System.StringComparer.OrdinalIgnoreCase);
        return all.Where(t => wanted.Contains(t.Name)).ToArray();
    }

    // Returns the delegated body's result — a *promise* for an async body —
    // straight back to JS, instead of unwrapping it in C#. The caller's
    // `await ctx.delegate(...)` then settles it under the SAME single outer pump
    // that already drives the crew run (JsCrew.UnwrapPromise), never a nested
    // blocking pump.
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
    // settle on the one engine thread under the outer pump, with no reentrancy
    // and no artificial settle ceiling. This makes concurrent delegation safe.
    //
    // The signature is synchronous (returns JsValue, not Task<JsValue>): the
    // guards throw synchronously at the call site and a JS `await` on the
    // returned promise does the waiting. A delegated body that rejects surfaces
    // through the outer pump as a normal promise rejection.
    public Func<JsAgent, JsValue, JsValue> @delegate => (target, input) =>
    {
        ArgumentNullException.ThrowIfNull(target);
        EnsureSelfInCrew();
        if (!_crew.has(target))
            throw new AgentNotInThisCrewException(target.name, _crew.name);
        if (target.Builder.BodyFunction is null || target.Builder.BodyFunction.IsUndefined()) return JsValue.Null;
        _ct.ThrowIfCancellationRequested();
        var inputJs = input ?? JsValue.Undefined;
        return _engine.Invoke(target.Builder.BodyFunction, [inputJs, JsValue.Undefined]);
    };

    public Action<JsAgent, JsValue> send => (target, message) =>
    {
        ArgumentNullException.ThrowIfNull(target);
        EnsureSelfInCrew();
        _channel.Send(target.name, message);
    };

    public Func<JsValue?, Task<JsValue>> receive => async options =>
    {
        EnsureSelfInCrew();
        TimeSpan? timeout = null;
        if (options is not null && options.IsObject())
        {
            var to = options.Get("timeout");
            if (to.IsNumber()) timeout = TimeSpan.FromMilliseconds(to.AsNumber());
            else if (to.IsString()) timeout = ParseDuration(to.AsString());
        }
        var raw = await _channel.ReceiveAsync(_self.name, timeout, _ct).ConfigureAwait(false);
        return raw is JsValue jv ? jv : JsValue.FromObject(_engine, raw);
    };

    public Action<JsValue> broadcast => message =>
    {
        EnsureSelfInCrew();
        _channel.Broadcast(_self.name, message, _crew.agents.Select(a => a.name));
    };

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
    private JsValue? _lockJs;

    internal JsCrewProxy(JsCrew crew, Engine engine) { _crew = crew; _engineRef = engine; }
    public JsAgent? findByName(string name) => _crew.findByName(name);
    public JsAgent? findById(string id) => _crew.findById(id);
    public IReadOnlyList<JsAgent> findByRole(string role) => _crew.findByRole(role);
    public bool has(JsAgent agent) => _crew.has(agent);
    public string name => _crew.name;

    // Implemented in JS — see comment on JsAgentContext.@lock for the rationale.
    public JsValue @lock => _lockJs ??= BuildLockFunction();

    private JsValue BuildLockFunction()
    {
        Func<string, Task<JsValue>> acquire = async name =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            var sem = _crew.GetCrewLock(name);
            await sem.WaitAsync().ConfigureAwait(false);
            return JsValue.Undefined;
        };
        Action<string> release = name =>
        {
            _crew.GetCrewLock(name).Release();
        };
        var factory = _engineRef.Evaluate("""
            (acquire, release) => async function lock(name, fn) {
                await acquire(name);
                try { return await fn(); }
                finally { release(name); }
            }
            """);
        return _engineRef.Invoke(factory, [acquire, release]);
    }
}

public sealed partial class JsLogger
{
    private readonly ILogger _logger;
    internal JsLogger(ILogger logger) { _logger = logger ?? NullLogger.Instance; }
    public void debug(string message) => LogDebugMessage(message);

    public void info(string message) => LogInfoMessage(message);
    public void warn(string message) => LogWarnMessage(message);
    public void error(string message) => LogErrorMessage(message);

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
