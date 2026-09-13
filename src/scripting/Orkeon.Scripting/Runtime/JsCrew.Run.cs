using System.Runtime.CompilerServices;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Microsoft.Extensions.Logging;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.ErrorPolicy;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// The crew's run loop, in JavaScript (SCR-25 T4): <see cref="run"/>, <see cref="runAgent"/> and
/// <see cref="runStream"/> are JS functions built once per crew from <see cref="RunModuleSource"/>;
/// the CLR supplies the helpers of <see cref="CrewRunHelpers"/> and drives the loop only from
/// <see cref="RunAsync"/>, a root pump on an engine at rest.
/// </summary>
#pragma warning disable IDE1006 // Member names match the JS surface
#pragma warning disable CS1591 // JS-interop mirror of Crew in Typings/crew.d.ts; that declaration is the contract scripts read.
public sealed partial class JsCrew
{
    /// <summary>
    /// The factory the run module comes from. Called once per crew with the helper record; the object
    /// it returns holds the script-facing functions, the host entry and the hook observer. Every line
    /// of it runs as a promise reaction on the thread draining the engine, whichever it is; nothing
    /// calls back into the CLR from any other thread, and nothing drains.
    /// </summary>
    private const string RunModuleSource = """
        (h) => {
            "use strict";
            // `h` is the crew's CrewRunHelpers: synchronous CLR helpers, each a JavaScript throw on
            // failure (JsHostError.Guard), plus three that return Tasks this loop awaits — cancelled,
            // acquire, delay. Every line here runs as a promise reaction on the thread draining the
            // engine, whichever it is; nothing calls back into the CLR from any other thread.
            const {
                open, start, endRun, snapshotAgents,
                cancelled, throwIfCancelled,
                bodyOf, onErrorOf, stateSeed,
                acquire, release, beginAgent, openAttempt, closeAttempt, endAgent,
                describeError, decideAction, delay,
                recordResult, finishRun,
                crewHookOf, hookContext, displayMessage, logHookFailure,
                resolveAgent,
            } = h;

            // One run's JS-side handle: the CLR scope plus `wait`, which races a script promise
            // against the run's cancellation so a cancelled run does not sit behind a body, a state
            // factory or an onError handler that ignores ctx.signal. `cancelled(scope)` is a Task that
            // FAULTS when the run token fires (never completes otherwise); its rejection reaches JS as
            // Jint's own ExecutionCanceledException, so it is re-raised through `throwIfCancelled` on
            // the engine thread and the run rejects with the same bridged OperationCanceledException as
            // a step check would.
            const enter = (scope) => {
                const stop = cancelled(scope).catch(() => { throwIfCancelled(scope); });
                return { scope, wait: (value) => Promise.race([value, stop]) };
            };

            // One agent, every attempt: the instance semaphore, a fresh AgentContext per attempt, the
            // body, the agent's onError policy. The acquisition is NOT raced: only the Task observes
            // the token, so `release` is never reached while the acquisition is still undecided.
            // The state factory and the onError handler are awaited here, in JS (they may be async).
            // With a handler, a failure goes through it while the semaphore is still held and the
            // context alive (the handler may read ctx; a retry delay waits inside the section): skip
            // and fallback settle here, retry loops after its delay (the finally below releases; the
            // next iteration re-acquires and re-creates the context), anything else rethrows the
            // ORIGINAL error. A cancelled run never consults onError (describeError returns null).
            const runAttempts = async (t, agent, input) => {
                const { scope, wait } = t;
                const body = bodyOf(agent);
                const onError = onErrorOf(agent);
                for (let attempt = 1; ; attempt++) {
                    throwIfCancelled(scope);
                    await acquire(scope, agent);
                    let a = null;
                    try {
                        const seed = stateSeed(agent);
                        const initial = typeof seed === "function" ? await wait(seed()) : seed;
                        a = openAttempt(scope, agent, initial, attempt);
                        try {
                            return await wait(body(input, a.ctx));
                        } catch (err) {
                            const errCtx = onError === undefined ? null : describeError(scope, agent, err, attempt);
                            if (errCtx === null) throw err;
                            const decision = decideAction(await wait(onError(errCtx, a.ctx)), attempt);
                            if (decision.kind === "return") return decision.value;
                            if (decision.kind !== "retry") throw err;
                            if (decision.delayMs > 0) await wait(delay(scope, decision.delayMs));
                        }
                    } finally {
                        if (a !== null) closeAttempt(scope, a);
                        release(scope, agent);
                    }
                }
            };

            // onCrewStart(ctx) / onCrewComplete(ctx, result) / onCrewError(ctx, message), awaited here
            // with today's arguments. start and complete race the cancellation; error does not — it
            // runs BECAUSE the run failed, cancellation included, and must be allowed to finish (a
            // CLR root pump bounds it with its grace, JsCrew.RunAsync).
            const crewHook = async (t, which, arg) => {
                const hook = crewHookOf(t.scope, which);
                if (hook === undefined) return;
                const ctx = hookContext(t.scope);
                const p = arg === undefined ? hook(ctx) : hook(ctx, arg);
                await (which === "error" ? p : t.wait(p));
            };

            // The failure path shared by run and runStream: onCrewError sees the displayed message,
            // its own failure is logged, and the ORIGINAL error is what the caller receives.
            const reportFailure = async (t, err) => {
                try { await crewHook(t, "error", displayMessage(err)); }
                catch (hookErr) { logHookFailure(t.scope, "onCrewError", hookErr); }
            };

            // The run itself, as the stream of its agent steps: run drains it, runStream hands it to
            // the script. The generator's return value is the CrewResult. Abandoning the stream
            // (`break` in a `for await`, or `it.return()`) runs the finally — endRun — and nothing
            // else: a run nobody finished consuming is neither completed nor failed, no crew hook fires.
            // `at` is Date.now() (epoch ms): crew.d.ts declares `at: number`.
            async function* steps(scope) {
                const t = enter(scope);
                try {
                    start(scope);
                    throwIfCancelled(scope);
                    await crewHook(t, "start");
                    for (const agent of snapshotAgents(scope)) {
                        throwIfCancelled(scope);
                        yield { type: "agent.start", payload: { name: agent.name }, at: Date.now() };
                        const step = beginAgent(scope, agent);
                        let output = null;
                        try {
                            if (bodyOf(agent) !== undefined) output = await runAttempts(t, agent, undefined);
                        } finally {
                            endAgent(scope, step);
                        }
                        recordResult(scope, step, output);
                        yield { type: "agent.stop", payload: { name: agent.name, output }, at: Date.now() };
                    }
                    const result = finishRun(scope);
                    await crewHook(t, "complete", result);
                    return result;
                } catch (err) {
                    await reportFailure(t, err);
                    throw err;
                } finally {
                    endRun(scope);
                }
            }

            // The run drained to its result. `scope` is a CrewRunScope: constructed by `open` for a
            // script caller, by JsCrew.RunAsync for a CLR caller (runFromHost) — the host keeps the
            // handle so it can abandon the run if the engine itself erupts.
            const runWith = async (scope) => {
                const it = steps(scope);
                for (;;) {
                    const { done, value } = await it.next();
                    if (done) return value;
                }
            };

            return {
                run: async (options) => runWith(open("run", options)),
                runFromHost: (scope) => runWith(scope),
                async *runStream(options) { return yield* steps(open("stream", options)); },

                // crew.d.ts: runAgent(agent: string | Agent, input, opts?). The agent runs under its
                // instance semaphore with an AgentContext and its own onError policy (chapter 05); no
                // crew hooks, no result recording, no budget, no span. Calling it on the agent whose
                // body is executing throws RecursiveAgentInvocationException (resolveAgent) instead
                // of deadlocking on the semaphore that body already holds.
                async runAgent(agentOrName, input, options) {
                    const scope = open("agent", options);
                    const t = enter(scope);
                    try {
                        start(scope);
                        const agent = resolveAgent(scope, agentOrName);
                        return bodyOf(agent) === undefined ? null : await runAttempts(t, agent, input);
                    } finally {
                        endRun(scope);
                    }
                },

                // Lifecycle hooks (onAgentStart / onAgentStop): the CLR hands a returned promise here
                // so its rejection is reported once (a CLR log call) and never left as an unobserved
                // rejection. Never awaited, never drained.
                observe: (promise, onRejected) => { Promise.resolve(promise).then(undefined, onRejected); },
            };
        }
        """;

    private static readonly Prepared<Acornima.Ast.Script> RunModule = Engine.PrepareScript(RunModuleSource);

    private static readonly ConditionalWeakTable<Engine, JsValue> Factories = new();

    /// <summary>
    /// The factory function, evaluated ONCE per engine. <c>CrewBuilderBinding.Register</c> calls this
    /// while the engine is at rest and its queue empty; the lazy fallback exists for bare engines in
    /// tests. Never evaluate it from a script's synchronous prefix: <c>Evaluate</c> drains queued jobs
    /// on its way out, which a mid-statement nested evaluation must not do. <c>Invoke</c> has no such
    /// drain, so the per-crew instantiation below is legal anywhere on the engine thread.
    /// </summary>
    internal static JsValue RunModuleFactory(Engine engine) => Factories.GetValue(engine, static e => e.Evaluate(RunModule));

    private JsValue? _runModule;

    /// <summary>JS <c>async (options?) =&gt; CrewResult</c>; see the class remarks.</summary>
    public JsValue run => RunModuleInstance.Get("run");

    /// <summary>JS <c>async (agentOrName, input, options?) =&gt; TOut</c>; see the class remarks.</summary>
    public JsValue runAgent => RunModuleInstance.Get("runAgent");

    /// <summary>JS <c>async function* (options?) =&gt; CrewStreamEvent</c>; see the class remarks.</summary>
    public JsValue runStream => RunModuleInstance.Get("runStream");

    private JsValue RunFromHost => RunModuleInstance.Get("runFromHost");

    private JsValue Observe => RunModuleInstance.Get("observe");

    private JsValue RunModuleInstance => _runModule ??= _engine.Invoke(RunModuleFactory(_engine), [BuildRunModule()]);

    /// <summary>
    /// The helper record the module destructures. Every synchronous body is bridged — uniformly,
    /// including the ones with no failure path of their own — so a CLR exception is a JavaScript throw
    /// the loop's <c>catch</c> and <c>finally</c> see; the three task-returning helpers reject natively
    /// (a faulted or cancelled task), and nothing could build a bridged Error on a pool thread anyway.
    /// </summary>
    private CrewRunHelpers BuildRunModule() => new()
    {
        open = (kind, options) => JsHostError.Guard(_engine, () =>
        {
            var (signal, timeout) = CrewRunOptions.From(options);
            return new CrewRunScope(this, ParseKind(kind), signal, timeout, ownedByHost: false, CancellationToken.None);
        }),
        start = scope => JsHostError.Guard(_engine, scope.Start),
        endRun = scope => JsHostError.Guard(_engine, scope.End),
        snapshotAgents = scope => JsHostError.Guard(_engine, () =>
        {
            scope.ThrowIfNotRunning();
            return scope.Agents;
        }),
        cancelled = scope => scope.Cancelled,
        throwIfCancelled = scope => JsHostError.Guard(_engine, () => scope.Token.ThrowIfCancellationRequested()),
        bodyOf = agent => JsHostError.Guard(_engine, () => Normalize(agent.Builder.BodyFunction)),
        onErrorOf = agent => JsHostError.Guard(_engine, () => Normalize(agent.Builder.OnErrorHandler)),
        stateSeed = agent => JsHostError.Guard(_engine, () => Normalize(agent.Builder.StateFactory)),
        acquire = (scope, agent) => scope.AcquireAsync(agent),
        release = (scope, agent) => JsHostError.Guard(_engine, () => scope.Release(agent)),
        beginAgent = (scope, agent) => JsHostError.Guard(_engine, () => scope.BeginAgent(agent)),
        openAttempt = (scope, agent, initial, attempt) => JsHostError.Guard(_engine, () => scope.OpenAttempt(agent, initial, attempt)),
        closeAttempt = (scope, attempt) => JsHostError.Guard(_engine, () => scope.CloseAttempt(attempt)),
        endAgent = (scope, step) => JsHostError.Guard(_engine, () => scope.EndAgent(step)),
        describeError = (scope, agent, err, attempt) => JsHostError.Guard(_engine, () => DescribeError(scope, agent, err, attempt)),
        decideAction = (raw, attempt) => JsHostError.Guard(_engine, () => DecideAction(raw, attempt)),
        delay = async (scope, ms) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(ms), scope.Token).ConfigureAwait(false);
            return JsValue.Undefined;
        },
        recordResult = (scope, step, output) => JsHostError.Guard(_engine, () => scope.Record(step, output)),
        finishRun = scope => JsHostError.Guard(_engine, scope.Finish),
        crewHookOf = (scope, which) => JsHostError.Guard(_engine, () => CrewHookOf(scope, which)),
        hookContext = scope => JsHostError.Guard(_engine,
            () => new JsExecutionContext(CreateEnvironment(_agents[0], scope.Budget, scope.Token))),
        displayMessage = err => JsHostError.Guard(_engine, () => DisplayMessage(err)),
        logHookFailure = (scope, hookName, err) => JsHostError.Guard(_engine,
            () => LogCrewHookFailed(_logger, name, hookName, DisplayMessage(err))),
        resolveAgent = (scope, value) => JsHostError.Guard(_engine, () => ResolveAgent(value)),
    };

    private static CrewRunKind ParseKind(string kind) => kind switch
    {
        "run" => CrewRunKind.Run,
        "stream" => CrewRunKind.Stream,
        "agent" => CrewRunKind.Agent,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown crew run kind."),
    };

    /// <summary><c>null</c>, JS <c>null</c> and <c>undefined</c> all become <c>undefined</c>, the one absence the loop tests for.</summary>
    private static JsValue Normalize(JsValue? value)
        => value is null || value.IsUndefined() || value.IsNull() ? JsValue.Undefined : value;

    internal static bool HasBody(JsAgent agent) => !Normalize(agent.Builder.BodyFunction).IsUndefined();

    /// <summary>
    /// The <c>err</c> an <c>onError</c> handler receives, or <c>null</c> once the run is cancelled — the
    /// loop then rethrows without consulting the policy. The code maps from the innermost CLR exception,
    /// so a faulted task's <c>AggregateException(ReceiveTimeoutException)</c> reads <c>receive_timeout</c>.
    /// </summary>
    private static JsErrorContext? DescribeError(CrewRunScope scope, JsAgent agent, JsValue err, int attempt)
    {
        if (scope.Token.IsCancellationRequested) return null;
        var inner = ToClrException(err);
        return new JsErrorContext(ErrorCodeMapper.MapToCode(inner), inner.Message, inner, attempt,
            new JsAgentRef(agent.id, agent.name));
    }

    /// <summary>
    /// A bridged Error or a wrapped CLR exception (a faulted task) unwraps to its innermost exception; a
    /// script's own throw becomes an <see cref="InvalidOperationException"/> carrying the JavaScript
    /// error's message, the shape a synchronous body's throw always had.
    /// </summary>
    private static Exception ToClrException(JsValue err)
    {
        if (JsHostError.Unwrap(err) is { } clr)
            return JsExceptionUnwrap.UnwrapToInnermost(clr);
        var thrown = new JavaScriptException(err);
        return new InvalidOperationException(thrown.Message, thrown);
    }

    /// <summary>
    /// The <c>onError</c> decision table: skip returns <c>null</c>, fallback returns its value, retry
    /// re-runs after its delay until <c>max</c> attempts were made, fail — and anything that is not an
    /// <c>ErrorAction</c> — rethrows the original error.
    /// </summary>
    private static ErrorDecision DecideAction(JsValue raw, int attempt)
    {
        var action = raw.ToObject() as JsErrorAction ?? new JsErrorAction(JsErrorActionKind.Fail);
        return action.kind switch
        {
            JsErrorActionKind.Skip => new ErrorDecision("return", JsValue.Null, 0),
            JsErrorActionKind.Fallback => new ErrorDecision("return", action.fallbackValue ?? JsValue.Undefined, 0),
            JsErrorActionKind.Retry when action.max is int max && attempt >= max => new ErrorDecision("rethrow", JsValue.Undefined, 0),
            JsErrorActionKind.Retry => new ErrorDecision("retry", JsValue.Undefined, action.delay?.TotalMilliseconds ?? 0),
            _ => new ErrorDecision("rethrow", JsValue.Undefined, 0),
        };
    }

    /// <summary>
    /// The crew hook the loop awaits, or <c>undefined</c>: none declared, a crew without agents (the
    /// hook context needs an anchor agent), or a <c>runAgent</c> scope (no crew hooks there).
    /// </summary>
    private JsValue CrewHookOf(CrewRunScope scope, string which)
    {
        if (scope.Kind == CrewRunKind.Agent || _agents.Count == 0) return JsValue.Undefined;
        var hook = which switch
        {
            "start" => _onCrewStart,
            "complete" => _onCrewComplete,
            "error" => _onCrewError,
            _ => throw new ArgumentOutOfRangeException(nameof(which), which, "Unknown crew hook."),
        };
        return Normalize(hook);
    }

    /// <summary>
    /// The message <c>onCrewError</c> receives and the hook-failure log carries: the innermost CLR
    /// message of a bridged or wrapped exception, a JavaScript error's <c>message</c>, or the value's
    /// text. A cancelled task rejects with Jint's own <see cref="ExecutionCanceledException"/>; it is
    /// displayed as the cancellation it is, the sentence a bridged <see cref="OperationCanceledException"/> shows.
    /// </summary>
    private static string DisplayMessage(JsValue err)
    {
        if (JsHostError.Unwrap(err) is { } clr)
        {
            return clr is ExecutionCanceledException
                ? new OperationCanceledException().Message
                : JsExceptionUnwrap.UnwrapToInnermost(clr).Message;
        }
        if (err.IsObject())
        {
            var message = err.AsObject().Get("message");
            if (message.IsString()) return message.AsString();
        }
        return err.ToString();
    }

    /// <summary>
    /// <c>runAgent</c>'s target: an <see cref="JsAgent"/> or an agent name, a member of this crew, and not
    /// the agent whose body is executing — chapter 05 forbids re-entrance in V1, and failing loudly beats
    /// waiting on the semaphore that body already holds. The attribution is one crew-wide value, so under
    /// two interleaved runs of one crew this check is best-effort: a false positive is loud, the
    /// alternative is silent.
    /// </summary>
    private JsAgent ResolveAgent(JsValue value)
    {
        var agent = value.ToObject() as JsAgent;
        if (agent is null && value.IsString())
            agent = findByName(value.AsString()) ?? throw new AgentNotInThisCrewException(value.AsString(), name);
        if (agent is null)
            throw new InvalidScriptException("crew.runAgent(agent, input) expects an Agent or an agent name.");
        if (!has(agent))
            throw new AgentNotInThisCrewException(agent.name, name);
        if (string.Equals(_eventBroker?.CurrentAgentId, agent.id, StringComparison.Ordinal))
            throw new RecursiveAgentInvocationException(agent.name);
        return agent;
    }

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Crew '{Crew}' hook {Hook} failed: {Reason}")]
    private static partial void LogCrewHookFailed(ILogger logger, string crew, string hook, string reason);
}
#pragma warning restore CS1591
#pragma warning restore IDE1006
