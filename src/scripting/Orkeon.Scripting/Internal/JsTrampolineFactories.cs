using System.Runtime.CompilerServices;
using Jint;
using Jint.Native;
using Orkeon.Scripting.Orchestration;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Internal;

/// <summary>
/// The JS trampoline factories — one per trampoline the runtime builds from a JavaScript source —
/// parsed once per process and evaluated once per engine, while that engine is at rest (SCR-25 T7).
/// </summary>
/// <remarks>
/// <para><c>Engine.Evaluate</c> drains the queued event-loop jobs on its way out; <c>Engine.Invoke</c>
/// does not (Jint 4.16.1: <c>ScriptEvaluation</c> ends with <c>RunAvailableContinuations</c>,
/// <c>ExecuteWithConstraints(DoInvoke)</c> does not). A factory evaluated lazily from a script's
/// synchronous prefix — the first <c>ctx.lock</c> reached from a lifecycle hook, the first
/// <c>fsm.send</c>, the first bridged throw — therefore ran the microtasks the prefix had already
/// queued in the middle of a statement, before the prefix was over. <see cref="JsEngineFactory.Create"/>
/// evaluates every factory through <see cref="Prepare"/>, with the engine at rest and its queue empty;
/// a site then only <c>Invoke</c>s its factory, which is legal anywhere on the engine thread — in the
/// prefix and inside a job alike.</para>
/// <para>A bare engine that did not go through the factory (a test's <c>new Engine()</c>) evaluates
/// a factory on its first use, which such tests reach with the engine at rest. The sources live next
/// to the class that owns their semantics; this class decides WHEN they are evaluated, and the
/// architecture guard (<c>EngineThreadingGuardTests</c>) keeps every other <c>Evaluate</c> out of the
/// runtime.</para>
/// </remarks>
internal static class JsTrampolineFactories
{
    public static readonly Factory HostError = new(JsHostError.FactorySource);
    public static readonly Factory Lock = new(LockFactorySource);
    public static readonly Factory StateWith = new(JsAgentContext.StateWithFactorySource);
    public static readonly Factory StateProxy = new(JsAgentContext.StateProxyFactorySource);
    public static readonly Factory Publish = new(JsEventTopic.PublishFactorySource);
    public static readonly Factory Graph = new(JsStateGraph.TrampolineSource);
    public static readonly Factory FsmSend = new(JsStateMachine.SendFactorySource);
    public static readonly Factory Act = new(JsLlmFacade.ActFactorySource);
    public static readonly Factory StreamIterable = new(JsLlmFacade.StreamIterableFactorySource);
    public static readonly Factory AsyncIterable = new(JsLlmFacade.AsyncIterableFactorySource);
    public static readonly Factory CrewRunModule = new(JsCrew.RunModuleSource);

    /// <summary>Every factory, the set <see cref="Prepare"/> evaluates.</summary>
    internal static IReadOnlyList<Factory> All { get; } =
    [
        HostError, Lock, StateWith, StateProxy, Publish, Graph, FsmSend, Act, StreamIterable, AsyncIterable, CrewRunModule,
    ];

    /// <summary>
    /// The named-lock trampoline shared by <c>ctx.lock</c>, <c>ctx.crew.lock</c> and a published
    /// event's <c>lock</c>: the semantics are one — acquire a semaphore with the body's token, run
    /// the callback, release in <c>finally</c> — and only the semaphore table behind
    /// <c>acquire</c> / <c>release</c> differs. Implemented in JS rather than C# because the callback's promise must settle under
    /// the pump that is already draining: a C# implementation would have to drain it from inside
    /// an engine callback, which Jint's single-drainer loop does not allow. The trampoline only
    /// awaits a real Task (<c>acquire</c>) and chains directly on the callback's promise.
    /// </summary>
    private const string LockFactorySource = """
        (acquire, release) => async function lock(name, fn) {
            await acquire(name);
            try { return await fn(); }
            finally { release(name); }
        }
        """;

    /// <summary>
    /// Evaluates every factory on <paramref name="engine"/>. Called by <see cref="JsEngineFactory.Create"/>
    /// right after the engine is built — at rest, job queue empty — so that no site ever evaluates from
    /// a script's synchronous prefix.
    /// </summary>
    public static void Prepare(Engine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        foreach (var factory in All)
            factory.For(engine);
    }

    /// <summary>
    /// One trampoline factory: its source parsed once per process, its evaluation cached per engine
    /// for as long as the engine lives.
    /// </summary>
    internal sealed class Factory
    {
        private readonly Prepared<Acornima.Ast.Script> _script;
        private readonly ConditionalWeakTable<Engine, JsValue> _perEngine = new();

        internal Factory(string source)
        {
            _script = Engine.PrepareScript(source);
        }

        /// <summary>
        /// The factory function on <paramref name="engine"/>: the value <see cref="Prepare"/> cached,
        /// or — for a bare engine that skipped it — evaluated now, with the engine at rest.
        /// </summary>
        public JsValue For(Engine engine) => _perEngine.GetValue(engine, Evaluate);

        private JsValue Evaluate(Engine engine) => engine.Evaluate(_script);
    }
}
