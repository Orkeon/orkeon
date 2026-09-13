using Jint;
using Jint.Native;
using Microsoft.Extensions.Logging;
using Orkeon.Scripting.Configuration;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Doubles;
using static Orkeon.Scripting.Tests.Testing.ScriptGlobals;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// The crew run loop in JavaScript (SCR-25 T4): what a cancelled run releases and still runs, how a
/// CLR caller and a script caller see a failure, <c>runAgent</c> under the semaphore with a context
/// and the re-entrance guard, <c>runStream</c> abandoned, retries and async policies, and a CLR
/// root pump refused from inside its own drain.
/// </summary>
/// <remarks>
/// A cancellation is raised from inside the body (<c>__cancel()</c>), never by a timer: under this
/// suite's own contention (24 blocking root pumps) a timer callback and every pool hop after it can
/// be late by seconds, and a wall-clock bound on the run then measures the pool, not the runtime.
/// That a cancelled run unwound cooperatively — inside <see cref="CrewRunScope.CancellationGrace"/>,
/// not abandoned at it — is read from the log instead: the host logs the abandonment (event 4) only
/// when the grace expired before the loop's own <c>finally</c> ran. A generous guard turns a hang
/// into a failure.
/// </remarks>
public sealed class JsCrewRunTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(20);

    private static Engine NewEngine(ILoggerFactory? loggerFactory = null)
        => new JsEngineFactory(loggerFactory: loggerFactory).Create();

    /// <summary>The run unwound on its own: the host never abandoned it at the grace (<c>JsCrew</c> event 4 is the abandon path's only log).</summary>
    private static void AssertNotAbandoned(RecordingLoggerFactory factory)
        => Assert.DoesNotContain(factory.Logger.Entries, e => e.Message.Contains("run abandoned by its host", StringComparison.Ordinal));

    private static T Eval<T>(Engine engine, string js) => (T)engine.Evaluate(js).ToObject()!;

    private static JsValue Js(Engine engine, string js) => engine.Evaluate(js);

    /// <summary>
    /// A cancelled run hands the agent's instance semaphore back through the loop's own
    /// <c>finally</c>: the next run of the same crew is not stuck behind it. The body parks on
    /// <c>ctx.receive</c> before it cancels, so the run is cancelled while a body await is pending.
    /// </summary>
    [Fact]
    public async Task Cancelled_run_releases_the_instance_semaphore_for_the_next_run()
    {
        using var factory = new RecordingLoggerFactory();
        var engine = NewEngine(factory);
        using var cts = InstallCancel(engine);
        engine.SetValue("quick", false);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    if (quick) return "fast";
                    const parked = ctx.receive({ timeout: 5000 });
                    __cancel();
                    return await parked;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => crew.RunAsync(null, cts.Token).WaitAsync(HangGuard, Ct));
        AssertNotAbandoned(factory);

        engine.SetValue("quick", true);
        var result = await crew.RunAsync(null, Ct).WaitAsync(HangGuard, Ct);

        Assert.Equal("fast", result.output);
    }

    /// <summary>
    /// A body that awaits a host operation ignoring <c>ctx.signal</c> does not hold the cancelled run:
    /// every script await is raced against the run's cancellation, so the caller gets its
    /// <see cref="OperationCanceledException"/> from the raced unwind — never from the host abandoning
    /// the run at <see cref="CrewRunScope.CancellationGrace"/> — and the crew is reusable, the zombie
    /// body left to its own unobserved chain.
    /// </summary>
    [Fact]
    public async Task Cancelled_run_whose_body_ignores_the_signal_returns_within_the_grace()
    {
        using var factory = new RecordingLoggerFactory();
        var engine = NewEngine(factory);
        InstallSleep(engine);
        using var cts = InstallCancel(engine);
        engine.SetValue("quick", false);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    if (quick) return "fast";
                    const parked = __sleep(60000);
                    __cancel();
                    await parked;
                    return "slow";
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => crew.RunAsync(null, cts.Token).WaitAsync(HangGuard, Ct));
        AssertNotAbandoned(factory);

        engine.SetValue("quick", true);
        var result = await crew.RunAsync(null, Ct).WaitAsync(HangGuard, Ct);

        Assert.Equal("fast", result.output);
    }

    /// <summary>
    /// A run opened from a body — here <c>crew.runAgent</c> without <c>{ signal: ctx.signal }</c> — is
    /// a child of the attempt that opened it: cancelling the outer run cancels it, and its instance
    /// semaphore comes back inside the outer run's unwind, not under a later drain. The nested loop
    /// races the outer run's <c>stop</c> promise, so its unwind runs in the drain pass that rejects the
    /// outer run, whichever of the two rejection jobs runs first; the count is read right after
    /// <see cref="JsCrew.RunAsync"/> threw, with no pump active.
    /// </summary>
    [Fact]
    public async Task Cancelled_run_cancels_the_runAgent_it_opened_and_releases_its_semaphore()
    {
        using var factory = new RecordingLoggerFactory();
        var engine = NewEngine(factory);
        InstallSleep(engine);
        using var cts = InstallCancel(engine);
        engine.SetValue("quick", false);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => quick ? "fast" : await crew.runAgent("B", {})).build();
            const b = agentBuilder().name("B").role("R").goal("G")
                .body(async (input, ctx) => {
                    if (quick) return "b";
                    const parked = __sleep(60000);
                    __cancel();
                    await parked;
                    return "slow";
                }).build();
            const crew = crewBuilder().withAgent(a).withAgent(b).build();
            crew;
            """);
        var semaphoreB = crew.GetInstanceSemaphore(crew.findByName("B")!);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => crew.RunAsync(null, cts.Token).WaitAsync(HangGuard, Ct));

        AssertNotAbandoned(factory);
        Assert.Equal(1, semaphoreB.CurrentCount);
        Assert.Null(crew.EventBroker.CurrentAgentId);
        engine.SetValue("quick", true);
        var result = await crew.RunAsync(null, Ct).WaitAsync(HangGuard, Ct);
        Assert.Equal("b", result.output);
    }

    /// <summary>
    /// The same link across crews: <c>await sub.run()</c> from a body, without <c>{ signal: ctx.signal }</c>,
    /// is a child of the innermost attempt open on the engine — the calling body's, whichever crew it
    /// belongs to; the sub-crew has no open attempt of its own to read. Cancelling the outer run
    /// cancels the sub-crew's run, its instance semaphore comes back inside the outer unwind, and the
    /// sub-crew runs again at once. Read per crew, the sub-crew's run was unlinked: it kept its
    /// semaphore for good, and every later run of the sub-crew deadlocked on it.
    /// </summary>
    [Fact]
    public async Task Cancelled_run_cancels_the_sub_crew_run_it_opened_and_releases_its_semaphore()
    {
        using var factory = new RecordingLoggerFactory();
        var engine = NewEngine(factory);
        InstallSleep(engine);
        using var cts = InstallCancel(engine);
        engine.SetValue("quick", false);
        var outer = Eval<JsCrew>(engine, """
            const y = agentBuilder().name("Y").role("R").goal("G")
                .body(async (input, ctx) => {
                    if (quick) return "y";
                    const parked = __sleep(60000);
                    __cancel();
                    await parked;
                    return "slow";
                }).build();
            globalThis.sub = crewBuilder().name("sub").withAgent(y).build();
            const x = agentBuilder().name("X").role("R").goal("G")
                .body(async (input, ctx) => quick ? "x" : (await sub.run()).output).build();
            crewBuilder().name("outer").withAgent(x).build();
            """);
        var sub = Eval<JsCrew>(engine, "sub");
        var semaphoreY = sub.GetInstanceSemaphore(sub.findByName("Y")!);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => outer.RunAsync(null, cts.Token).WaitAsync(HangGuard, Ct));

        AssertNotAbandoned(factory);
        Assert.Equal(1, semaphoreY.CurrentCount);
        Assert.Null(sub.EventBroker.CurrentAgentId);
        engine.SetValue("quick", true);
        var result = await sub.RunAsync(null, Ct).WaitAsync(HangGuard, Ct);
        Assert.Equal("y", result.output);
    }

    /// <summary>
    /// The broker's attribution is the set of open attempts, not a stack of previous values: a fan-out
    /// whose attempts close in opening order (A before B) leaves an idle crew attributing nothing, so a
    /// later top-level <c>runAgent("A")</c> is not mistaken for re-entrance. Restoring "what was
    /// current when this attempt opened" handed A back when B closed, for good.
    /// </summary>
    [Fact]
    public async Task Fan_out_closing_in_opening_order_leaves_no_attribution_behind()
    {
        var engine = NewEngine();
        InstallSleep(engine);
        var crew = Eval<JsCrew>(engine, """
            let releaseB; const gateB = new Promise(resolve => { releaseB = resolve; });
            const a = agentBuilder().name("A").role("R").goal("G").body(async () => { await __sleep(10); return "a"; }).build();
            const b = agentBuilder().name("B").role("R").goal("G").body(async () => { await gateB; return "b"; }).build();
            globalThis.crew = crewBuilder().withAgent(a).withAgent(b).build();
            """);

        var outcome = await engine.EvaluateAsync("""
            (async () => {
                const pa = crew.runAgent("A", {});
                const pb = crew.runAgent("B", {});
                await pa;                 // A closes while B is still open
                releaseB();
                await pb;
                return await crew.runAgent("A", {});
            })()
            """, cancellationToken: Ct);

        Assert.Equal("a", outcome.AsString());
        Assert.Null(crew.EventBroker.CurrentAgentId);
        Assert.False(crew.EventBroker.CurrentCt.CanBeCanceled);
    }

    /// <summary>
    /// The same closing order with a timed-out first attempt: the token a later attempt's close leaves
    /// behind is none, never the dead run's cancelled one — which <c>stateGraph.run()</c>, a topic
    /// handler's <c>ev.lock</c> and a run opened from a body would all read as ambient cancellation.
    /// </summary>
    [Fact]
    public async Task Timed_out_attempt_closing_first_leaves_no_cancelled_token_behind()
    {
        var engine = NewEngine();
        InstallSleep(engine);
        var crew = Eval<JsCrew>(engine, """
            let releaseB; const gateB = new Promise(resolve => { releaseB = resolve; });
            const a = agentBuilder().name("A").role("R").goal("G").body(async () => { await __sleep(60000); return "a"; }).build();
            const b = agentBuilder().name("B").role("R").goal("G").body(async () => { await gateB; return "b"; }).build();
            globalThis.crew = crewBuilder().withAgent(a).withAgent(b).build();
            """);

        var outcome = await engine.EvaluateAsync("""
            (async () => {
                const pa = crew.runAgent("A", {}, { timeout: "50ms" });
                const pb = crew.runAgent("B", {});
                const first = await pa.then(() => "no-throw", e => e.clrType);
                releaseB();
                return first + "," + await pb;
            })()
            """, cancellationToken: Ct);

        Assert.Equal("OperationCanceledException,b", outcome.AsString());
        Assert.Null(crew.EventBroker.CurrentAgentId);
        Assert.False(crew.EventBroker.CurrentCt.CanBeCanceled);
    }

    /// <summary>
    /// A run the host abandoned — a raw CLR throw from a non-bridged delegate inside the body erupts
    /// from the drain — has its stranded chain unwound under the engine's next drain. That chain
    /// fires no crew hook: the failure was the host's to report, and the next run's own hooks are
    /// the only ones it sees. <c>crew.add</c> is bridged since SCR-25 T7, so the raw throw comes from a
    /// host-installed delegate — the shape a host binding can still produce.
    /// </summary>
    [Fact]
    public async Task Abandoned_run_fires_no_crew_hook_under_a_later_drain()
    {
        var engine = NewEngine();
        var log = InstallLog(engine);
        engine.SetValue("quick", false);
        engine.SetValue("__raw", new Action(() => throw new InvalidOperationException("raw host failure")));
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    if (quick) { __log("body"); return "fast"; }
                    await Promise.resolve();
                    __raw();
                    return "unreachable";
                }).build();
            const crew = crewBuilder().name("abandoned").withAgent(a)
                .onCrewError((ctx, msg) => { __log("error:" + msg); })
                .onCrewComplete((ctx, result) => { __log("complete:" + result.output); })
                .build();
            crew;
            """);

        var raw = await Assert.ThrowsAsync<InvalidOperationException>(() => crew.RunAsync(null, Ct));
        Assert.Equal("raw host failure", raw.Message);
        Assert.Empty(log);

        engine.SetValue("quick", true);
        var result = await crew.RunAsync(null, Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.Equal("fast", result.output);
        Assert.Equal(["body", "complete:fast"], log);
    }

    /// <summary>
    /// The cooperative unwind a CLR caller's drain allows after cancellation: the body's own
    /// <c>finally</c> runs, <c>onCrewError</c> runs with the cancellation message and is awaited even
    /// though the run is cancelled (it is not raced), and the caller still sees the cancellation.
    /// Draining on the run token itself skipped all of it. The body parks on <c>ctx.receive</c>, and
    /// the hook releases it as well: the receive's own cancellation reaches JS through a pool
    /// continuation, and the proof that nothing was abandoned must not wait on the pool.
    /// </summary>
    [Fact]
    public async Task Cancelled_run_runs_its_finally_blocks_and_onCrewError()
    {
        using var factory = new RecordingLoggerFactory();
        var engine = NewEngine(factory);
        var log = InstallLog(engine);
        using var cts = InstallCancel(engine);
        var crew = Eval<JsCrew>(engine, """
            let bodyDone; const bodySettled = new Promise(resolve => { bodyDone = resolve; });
            let releaseBody; const bodyGate = new Promise(resolve => { releaseBody = resolve; });
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    try { const parked = ctx.receive({ timeout: 5000 }); __cancel(); await Promise.race([parked, bodyGate]); }
                    finally { __log("finally"); bodyDone(); }
                }).build();
            crewBuilder().withAgent(a)
                .onCrewError(async (ctx, msg) => { __log("error:" + msg); releaseBody(); await bodySettled; __log("error-done"); })
                .build();
            """);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => crew.RunAsync(null, cts.Token).WaitAsync(HangGuard, Ct));

        AssertNotAbandoned(factory);
        Assert.Contains("finally", log);
        Assert.Contains(log, e => e.StartsWith("error:", StringComparison.Ordinal) && e.Contains("canceled", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("error-done", log[^1]);
    }

    /// <summary>A cancelled run never consults <c>onError</c>: the policy is for failures, not for the caller's own cancellation.</summary>
    [Fact]
    public async Task Cancellation_bypasses_onError()
    {
        using var factory = new RecordingLoggerFactory();
        var engine = NewEngine(factory);
        using var cts = InstallCancel(engine);
        engine.SetValue("__onError", 0);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => { const parked = ctx.receive({ timeout: 5000 }); __cancel(); return await parked; })
                .onError((err, ctx) => { __onError += 1; return ErrorAction.retry({ max: 10 }); })
                .build();
            crewBuilder().withAgent(a).build();
            """);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => crew.RunAsync(null, cts.Token).WaitAsync(HangGuard, Ct));

        AssertNotAbandoned(factory);
        Assert.Equal(0d, engine.GetValue("__onError").AsNumber());
    }

    /// <summary>
    /// A cancellation the loop meets on a Task it awaits directly — the instance semaphore, held by
    /// another run — rejects the script caller with the same shape as a raced body await: the bridged
    /// <see cref="OperationCanceledException"/> with its <c>clrType</c>, not Jint's raw
    /// <c>ExecutionCanceledException</c> object a cancelled Task is marshalled as.
    /// </summary>
    [Fact]
    public async Task Cancellation_met_on_the_semaphore_acquisition_rejects_with_the_bridged_shape()
    {
        var engine = NewEngine();
        InstallSleep(engine);
        var outer = Eval<JsCrew>(engine, """
            let release; const gate = new Promise(resolve => { release = resolve; });
            const a = agentBuilder().name("A").role("R").goal("G").body(async () => { await gate; return "a"; }).build();
            const inner = crewBuilder().name("inner").withAgent(a).build();
            const describe = (e) => e && e.clrType ? "bridged:" + e.clrType : "raw:" + String(e);
            const o = agentBuilder().name("O").role("R").goal("G").body(async () => {
                // The body parks on the gate: a raced body await meets the timeout.
                const raced = await inner.run({ timeout: 50 }).then(() => "no-throw", describe);
                // A holder parks on the gate under A's semaphore: the next run meets its timeout on the acquisition.
                const holder = inner.run();
                await __sleep(10);
                const queued = await inner.run({ timeout: 50 }).then(() => "no-throw", describe);
                release();
                await holder;
                return raced + "|" + queued;
            }).build();
            crewBuilder().name("outer").withAgent(o).build();
            """);

        var result = await outer.RunAsync(null, Ct).WaitAsync(HangGuard, Ct);

        Assert.Equal("bridged:OperationCanceledException|bridged:OperationCanceledException", result.output);
    }

    /// <summary>
    /// <c>onCrewError</c> failing does not replace the failure it was told about: the caller receives
    /// the body's error, the hook's own failure is logged.
    /// </summary>
    [Fact]
    public async Task Original_body_error_is_thrown_when_onCrewError_itself_throws()
    {
        using var factory = new RecordingLoggerFactory();
        var engine = NewEngine(factory);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(() => { throw new Error("body-boom"); }).build();
            crewBuilder().name("fragile").withAgent(a)
                .onCrewError((ctx, msg) => { throw new Error("hook-boom"); })
                .build();
            """);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => crew.RunAsync(null, Ct));

        Assert.Contains("body-boom", ChainText(ex), StringComparison.Ordinal);
        Assert.DoesNotContain("hook-boom", ChainText(ex), StringComparison.Ordinal);
        Assert.Contains(factory.Logger.Entries, e => e.Level == LogLevel.Error
            && e.Message.Contains("onCrewError", StringComparison.Ordinal)
            && e.Message.Contains("hook-boom", StringComparison.Ordinal)
            && e.Message.Contains("'fragile'", StringComparison.Ordinal));
    }

    /// <summary>A failing <c>onCrewStart</c> is a failed run: no body runs, <c>onCrewError</c> sees the failure.</summary>
    [Fact]
    public async Task onCrewStart_failure_fails_the_run_and_reaches_onCrewError()
    {
        var engine = NewEngine();
        var log = InstallLog(engine);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(() => { __log("body"); return "ok"; }).build();
            crewBuilder().withAgent(a)
                .onCrewStart((ctx) => { throw new Error("start-boom"); })
                .onCrewError((ctx, msg) => { __log("error:" + msg); })
                .build();
            """);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => crew.RunAsync(null, Ct));

        Assert.Contains("start-boom", ChainText(ex), StringComparison.Ordinal);
        Assert.Equal(["error:start-boom"], log);
    }

    /// <summary>
    /// A bad <c>timeout</c> option is parsed before the loop starts: a CLR caller gets the raw
    /// <see cref="FormatException"/>, a script caller a rejected promise carrying it.
    /// </summary>
    [Fact]
    public async Task A_bad_timeout_option_reaches_a_CLR_caller_as_FormatException()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G").body(() => "x").build();
            globalThis.crew = crewBuilder().withAgent(a).build();
            """);

        await Assert.ThrowsAsync<FormatException>(() => crew.RunAsync(Js(engine, "({ timeout: 'abc' })"), Ct));

        var fromScript = await engine.EvaluateAsync(
            "(async () => { try { await crew.run({ timeout: 'abc' }); return 'no-throw'; } catch (e) { return e.clrType; } })()",
            cancellationToken: Ct);
        Assert.Equal("FormatException", fromScript.AsString());
    }

    /// <summary>
    /// <c>runAgent</c> takes an agent or its name, runs the target under its instance semaphore with a
    /// context (state seeded, <c>input</c> passed) and its own policy, and hands the result back.
    /// </summary>
    [Fact]
    public async Task runAgent_runs_the_target_under_its_semaphore_with_a_context_by_name_or_agent()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const b = agentBuilder().name("B").role("R").goal("G")
                .withState(() => ({ k: 3 }))
                .body((input, ctx) => (input ? input.x : 1) * ctx.state.k).build();
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const byName = await crew.runAgent("B", { x: 2 });
                    const byAgent = await crew.runAgent(ctx.crew.findByName("B"), { x: 3 });
                    return byName + "," + byAgent;
                }).build();
            const crew = crewBuilder().withAgent(a).withAgent(b).build();
            crew;
            """);

        var result = await crew.RunAsync(null, Ct);

        Assert.Equal("6,9", result.tasks[0].output!.ToString());
        Assert.Equal(3d, Convert.ToDouble(result.tasks[1].output, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Re-entrance is forbidden (chapter 05): it fails loudly instead of waiting on the semaphore the caller holds.</summary>
    [Fact]
    public async Task runAgent_of_the_running_agent_throws_RecursiveAgentInvocationException()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => await crew.runAgent("A", {})).build();
            const crew = crewBuilder().withAgent(a).build();
            crew;
            """);

        var ex = await Assert.ThrowsAsync<RecursiveAgentInvocationException>(
            () => crew.RunAsync(null, Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct));

        Assert.Contains("A", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>crew.add</c> and <c>crew.remove</c> reached from a body past its first await — inside an
    /// event-loop job — are JavaScript throws (SCR-25 T7): the body's <c>catch</c> sees the typed
    /// exception on <c>clrType</c> and the run completes, where a raw CLR throw skipped the catch and
    /// abandoned the run.
    /// </summary>
    [Fact]
    public async Task Duplicate_add_and_foreign_remove_inside_a_body_after_an_await_are_caught_by_the_body()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const foreign = agentBuilder().name("F").role("R").goal("G").build();
            crewBuilder().name("other").withAgent(foreign).build();
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    await Promise.resolve();
                    const seen = [];
                    try { crew.add(agentBuilder().name("A").role("R").goal("G").build()); seen.push("add:no-throw"); }
                    catch (e) { seen.push("add:" + e.clrType); }
                    try { crew.remove(foreign); seen.push("remove:no-throw"); }
                    catch (e) { seen.push("remove:" + e.clrType); }
                    finally { seen.push("finally"); }
                    return seen.join(",");
                }).build();
            const crew = crewBuilder().name("bridged").withAgent(a).build();
            crew;
            """);

        var result = await crew.RunAsync(null, Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.Equal($"add:{nameof(DuplicateAgentNameException)},remove:{nameof(AgentNotInThisCrewException)},finally", result.output);
        Assert.Single(crew.agents);
    }

    /// <summary>An agent of another crew is refused with the typed exception on the rejected value's <c>clr</c>.</summary>
    [Fact]
    public async Task runAgent_of_a_foreign_agent_throws_AgentNotInThisCrewException()
    {
        var engine = NewEngine();

        var clrType = await engine.EvaluateAsync("""
            const foreign = agentBuilder().name("F").role("R").goal("G").body(() => "f").build();
            const other = crewBuilder().name("other").withAgent(foreign).build();
            const crew = crewBuilder().name("main").withAgent(agentBuilder().name("A").role("R").goal("G").build()).build();
            (async () => { try { await crew.runAgent(foreign, {}); return "no-throw"; } catch (e) { return e.clrType; } })()
            """, cancellationToken: Ct);

        Assert.Equal(nameof(AgentNotInThisCrewException), clrType.AsString());
    }

    /// <summary>
    /// Breaking out of <c>runStream</c> ends the run without completing it: no further agent runs, no
    /// <c>onCrewComplete</c>, and the crew's semaphores and contexts are released, so the crew runs again.
    /// </summary>
    [Fact]
    public async Task runStream_break_ends_the_run_without_completing_it_and_the_crew_is_reusable()
    {
        var engine = NewEngine();
        var log = InstallLog(engine);
        var crew = Eval<JsCrew>(engine, """
            const inner = crewBuilder().name("inner")
                .withAgent(agentBuilder().name("i1").role("R").goal("G").body(() => { __log("i1"); return "1"; }).build())
                .withAgent(agentBuilder().name("i2").role("R").goal("G").body(() => { __log("i2"); return "2"; }).build())
                .onCrewComplete((ctx, result) => { __log("complete:" + result.output); })
                .build();
            const o = agentBuilder().name("o").role("R").goal("G")
                .body(async (input, ctx) => {
                    for await (const e of inner.runStream()) { __log(e.type); break; }
                    const r = await inner.run();
                    return r.output;
                }).build();
            crewBuilder().name("outer").withAgent(o).build();
            """);

        var result = await crew.RunAsync(null, Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.Equal("2", result.output);
        Assert.Equal(["agent.start", "i1", "i2", "complete:2"], log);
    }

    /// <summary>
    /// A retry is a new attempt: the context is rebuilt (state re-seeded, the first attempt's mutation
    /// gone) after the semaphore was released and re-acquired.
    /// </summary>
    [Fact]
    public async Task Retry_recreates_the_context_and_reacquires_the_semaphore()
    {
        var engine = NewEngine();
        engine.SetValue("__attempts", 0);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .withState(() => ({ n: 0 }))
                .body(async (input, ctx) => {
                    __attempts += 1;
                    if (__attempts === 1) { await ctx.state.with(s => ({ n: s.n + 1 })); throw new Error("first"); }
                    return String(ctx.state.n);
                })
                .onError((err, ctx) => ErrorAction.retry({ max: 3 }))
                .build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, Ct);

        Assert.Equal("0", result.tasks[0].output!.ToString());
        Assert.Equal(2d, engine.GetValue("__attempts").AsNumber());
    }

    /// <summary>An async state factory and an async <c>onError</c> are awaited by the loop, in JS.</summary>
    [Fact]
    public async Task Async_state_factory_and_async_onError_are_awaited()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .withState(async () => { await Promise.resolve(); return { n: 1 }; })
                .body((input, ctx) => { if (ctx.state.n !== 1) return "state-not-awaited"; throw new Error("boom"); })
                .onError(async (err, ctx) => { await Promise.resolve(); return ErrorAction.skip(); })
                .build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.Null(result.tasks[0].output);
    }

    /// <summary>
    /// <see cref="JsCrew.RunAsync"/> is a root pump: called from a CLR delegate the script invoked — on
    /// the thread draining this engine — it refuses instead of deadlocking on the gate it already holds.
    /// The delegate catches, so the failure is the body's return value and nothing erupts raw.
    /// </summary>
    [Fact]
    public async Task RunAsync_from_inside_the_engines_own_drain_throws_InvalidOperationException()
    {
        var engine = NewEngine();
        JsCrew? crew = null;
        engine.SetValue("__reenter", new Func<string>(() =>
        {
            try
            {
                crew!.RunAsync(null, Ct).GetAwaiter().GetResult();
                return "ran";
            }
            catch (Exception e)
            {
                return e.GetType().Name;
            }
        }));
        crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G").body(() => __reenter()).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, Ct).WaitAsync(TimeSpan.FromSeconds(5), Ct);

        Assert.Equal(nameof(InvalidOperationException), result.output);
    }

    /// <summary>
    /// A failure that arrived as a faulted <c>Task</c> — a <c>ctx.receive({ timeout })</c> that expired —
    /// reaches <c>onError</c> with the code of the innermost CLR exception (<c>receive_timeout</c>),
    /// not <c>unknown</c> for the <see cref="AggregateException"/> Jint rejects the await with.
    /// </summary>
    [Fact]
    public async Task Faulted_task_failure_reaches_onError_with_the_innermost_code()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => await ctx.receive({ timeout: 20 }))
                .onError((err, ctx) => ErrorAction.fallback(err.code + "|" + err.message))
                .build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, Ct).WaitAsync(HangGuard, Ct);

        Assert.StartsWith("receive_timeout|", result.output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A run started on an already-cancelled <c>signal</c> never reaches <c>onCrewStart</c> nor a body,
    /// and <c>onCrewError</c> still runs — it runs because the run failed, cancellation included.
    /// </summary>
    [Fact]
    public async Task Already_cancelled_signal_skips_onCrewStart_and_reaches_onCrewError()
    {
        var engine = NewEngine();
        var log = InstallLog(engine);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        engine.SetValue("__dead", cts.Token);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G").body(() => { __log("body"); return "x"; }).build();
            globalThis.crew = crewBuilder().withAgent(a)
                .onCrewStart((ctx) => { __log("start"); })
                .onCrewError((ctx, msg) => { __log("error:" + msg); })
                .build();
            """);

        var outcome = await engine.EvaluateAsync(
            "(async () => { try { await crew.run({ signal: __dead }); return 'no-throw'; } catch (e) { return e.clrType; } })()",
            cancellationToken: Ct);

        Assert.Equal(nameof(OperationCanceledException), outcome.AsString());
        Assert.Single(log);
        Assert.StartsWith("error:", log[0], StringComparison.Ordinal);
        Assert.Contains("canceled", log[0], StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// <c>ExecutionTimeout</c> spans a whole CLR-driven run: two bodies that each await a host task
    /// shorter than the limit exceed it together. It used to re-arm per agent body, which let a run
    /// of any length through under a limit meant as the sandbox's ceiling.
    /// </summary>
    [Fact]
    public async Task ExecutionTimeout_spans_the_whole_CLR_driven_run()
    {
        var engine = new JsEngineFactory(new ScriptingLimitsOptions { ExecutionTimeout = TimeSpan.FromMilliseconds(300) }).Create();
        InstallSleep(engine);
        var crew = Eval<JsCrew>(engine, """
            const mk = (n) => agentBuilder().name(n).role("R").goal("G").body(async () => { await __sleep(200); return n; }).build();
            crewBuilder().withAgent(mk("A")).withAgent(mk("B")).build();
            """);

        await Assert.ThrowsAsync<TimeoutException>(() => crew.RunAsync(null, Ct).WaitAsync(HangGuard, Ct));
    }

    /// <summary>
    /// <c>runAgent</c> applies the target's own <c>onError</c> policy: a throwing target with a
    /// fallback hands the fallback to the caller, exactly as the crew loop would.
    /// </summary>
    [Fact]
    public async Task runAgent_applies_the_targets_own_onError_policy()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const b = agentBuilder().name("B").role("R").goal("G")
                .body(() => { throw new Error("b-boom"); })
                .onError((err, ctx) => ErrorAction.fallback("fallback:" + err.message))
                .build();
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => await crew.runAgent("B", {})).build();
            const crew = crewBuilder().withAgent(a).withAgent(b).build();
            crew;
            """);

        var result = await crew.RunAsync(null, Ct).WaitAsync(HangGuard, Ct);

        Assert.Equal("fallback:b-boom", result.output);
    }

    /// <summary><c>runAgent</c> honours <c>opts.signal</c>: a cancelled signal rejects the call with the bridged cancellation, before the target's body runs.</summary>
    [Fact]
    public async Task runAgent_honours_the_signal_option()
    {
        var engine = NewEngine();
        var log = InstallLog(engine);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        engine.SetValue("__dead", cts.Token);

        var outcome = await engine.EvaluateAsync("""
            const b = agentBuilder().name("B").role("R").goal("G").body(() => { __log("b"); return "b"; }).build();
            const crew = crewBuilder().withAgent(b).build();
            (async () => { try { await crew.runAgent("B", {}, { signal: __dead }); return "no-throw"; } catch (e) { return e.clrType; } })()
            """, cancellationToken: Ct);

        Assert.Equal(nameof(OperationCanceledException), outcome.AsString());
        Assert.Empty(log);
    }

    /// <summary>
    /// <c>runStream</c> consumed to its end is the run: the crew hooks fire around it as they do for
    /// <c>run</c>, and each event's <c>at</c> is epoch milliseconds (<c>Date.now()</c>) inside the
    /// window the stream was consumed in.
    /// </summary>
    [Fact]
    public async Task runStream_consumed_to_its_end_fires_the_crew_hooks_and_stamps_epoch_milliseconds()
    {
        var engine = NewEngine();
        var log = InstallLog(engine);
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var stamps = await engine.EvaluateAsync("""
            const a = agentBuilder().name("A").role("R").goal("G").body(() => "x").build();
            const crew = crewBuilder().withAgent(a)
                .onCrewStart((ctx) => { __log("start"); })
                .onCrewComplete((ctx, result) => { __log("complete:" + result.output); })
                .build();
            (async () => { const at = []; for await (const e of crew.runStream()) at.push(e.at); return at; })()
            """, cancellationToken: Ct);

        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.Equal(["start", "complete:x"], log);
        var values = ((object[])stamps.ToObject()!).Select(v => Convert.ToInt64(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(2, values.Length);
        Assert.All(values, at => Assert.InRange(at, before, after));
    }
}
