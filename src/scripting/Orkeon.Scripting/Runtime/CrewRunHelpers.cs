using Jint.Native;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.ErrorPolicy;

namespace Orkeon.Scripting.Runtime;

/// <summary>
/// The CLR helpers the crew's JavaScript run module destructures by name (SCR-25 T4, one record per
/// <see cref="JsCrew"/>). Every member is <c>required</c>: a forgotten helper is a compile error, not
/// an <c>undefined is not a function</c> at run time. The synchronous ones are bridged
/// (<c>JsHostError.Guard</c>) so a failure is a JavaScript throw the loop's <c>catch</c> and
/// <c>finally</c> see; the three that return a <see cref="Task"/> — <c>cancelled</c>, <c>acquire</c>,
/// <c>delay</c> — reject natively when the task faults or is cancelled, and are the only awaits the
/// loop makes on the CLR. Every call reaches the CLR on the thread draining the engine.
/// </summary>
#pragma warning disable IDE1006 // property names are the JS surface the module destructures
internal sealed record CrewRunHelpers
{
    /// <summary><c>(kind, options) =&gt; scope</c> — parses the options and builds a script-owned scope.</summary>
    public required Func<string, JsValue, CrewRunScope> open { get; init; }

    /// <summary><c>(scope)</c> — budget, span, shape warning, agent snapshot.</summary>
    public required Action<CrewRunScope> start { get; init; }

    /// <summary><c>(scope)</c> — the loop's <c>finally</c>: <see cref="CrewRunScope.End"/>.</summary>
    public required Action<CrewRunScope> endRun { get; init; }

    /// <summary><c>(scope) =&gt; Agent[]</c> — the run's agent snapshot, as a JS array.</summary>
    public required Func<CrewRunScope, JsAgent[]> snapshotAgents { get; init; }

    /// <summary><c>(scope) =&gt; Task</c> — faults when the run's token fires; the loop's <c>stop</c> promise.</summary>
    public required Func<CrewRunScope, Task<JsValue>> cancelled { get; init; }

    /// <summary><c>(scope)</c> — throws the bridged <see cref="OperationCanceledException"/> once the run's token fired.</summary>
    public required Action<CrewRunScope> throwIfCancelled { get; init; }

    /// <summary><c>(agent) =&gt; body | undefined</c>.</summary>
    public required Func<JsAgent, JsValue> bodyOf { get; init; }

    /// <summary><c>(agent) =&gt; onError | undefined</c>.</summary>
    public required Func<JsAgent, JsValue> onErrorOf { get; init; }

    /// <summary><c>(agent) =&gt; factory | seed | undefined</c> — the loop calls (and awaits) a function, uses anything else as is.</summary>
    public required Func<JsAgent, JsValue> stateSeed { get; init; }

    /// <summary><c>(scope, agent) =&gt; Task</c> — the agent's instance semaphore, cancelled by the run's token.</summary>
    public required Func<CrewRunScope, JsAgent, Task<JsValue>> acquire { get; init; }

    /// <summary><c>(scope, agent)</c> — hands the instance semaphore back.</summary>
    public required Action<CrewRunScope, JsAgent> release { get; init; }

    /// <summary><c>(scope, agent) =&gt; step</c> — opens the agent's span.</summary>
    public required Func<CrewRunScope, JsAgent, AgentStep> beginAgent { get; init; }

    /// <summary><c>(scope, agent, initialState, attempt) =&gt; attempt</c> — a fresh context and the broker attribution; the loop reads <c>attempt.ctx</c>.</summary>
    public required Func<CrewRunScope, JsAgent, JsValue, int, AgentAttempt> openAttempt { get; init; }

    /// <summary><c>(scope, attempt)</c> — restores the attribution, disposes the context; idempotent.</summary>
    public required Action<CrewRunScope, AgentAttempt> closeAttempt { get; init; }

    /// <summary><c>(scope, step)</c> — closes the agent's span; idempotent.</summary>
    public required Action<CrewRunScope, AgentStep> endAgent { get; init; }

    /// <summary><c>(scope, agent, err, attempt) =&gt; ErrorContext | null</c> — null once the run is cancelled, so the policy is never consulted.</summary>
    public required Func<CrewRunScope, JsAgent, JsValue, int, JsErrorContext?> describeError { get; init; }

    /// <summary><c>(action, attempt) =&gt; { kind, value, delayMs }</c> — the <c>onError</c> decision table.</summary>
    public required Func<JsValue, int, ErrorDecision> decideAction { get; init; }

    /// <summary><c>(scope, ms) =&gt; Task</c> — a retry delay, cancelled by the run's token.</summary>
    public required Func<CrewRunScope, double, Task<JsValue>> delay { get; init; }

    /// <summary><c>(scope, step, output)</c> — records the agent's task result.</summary>
    public required Action<CrewRunScope, AgentStep, JsValue> recordResult { get; init; }

    /// <summary><c>(scope) =&gt; CrewResult</c>.</summary>
    public required Func<CrewRunScope, JsCrewResult> finishRun { get; init; }

    /// <summary><c>(scope, "start" | "complete" | "error") =&gt; hook | undefined</c>.</summary>
    public required Func<CrewRunScope, string, JsValue> crewHookOf { get; init; }

    /// <summary><c>(scope) =&gt; ctx</c> — a fresh execution context for one crew-hook invocation.</summary>
    public required Func<CrewRunScope, JsExecutionContext> hookContext { get; init; }

    /// <summary><c>(err) =&gt; string</c> — the message <c>onCrewError</c> receives.</summary>
    public required Func<JsValue, string> displayMessage { get; init; }

    /// <summary><c>(scope, hookName, err)</c> — logs a crew hook's own failure.</summary>
    public required Action<CrewRunScope, string, JsValue> logHookFailure { get; init; }

    /// <summary><c>(scope, agentOrName) =&gt; agent</c> — <c>runAgent</c>'s target, with the membership and re-entrance checks.</summary>
    public required Func<CrewRunScope, JsValue, JsAgent> resolveAgent { get; init; }
}
#pragma warning restore IDE1006
