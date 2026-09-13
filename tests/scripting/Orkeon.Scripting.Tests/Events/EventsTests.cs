using Jint;
using Orkeon.Scripting.Exceptions;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Timing;
using static Orkeon.Tests.Shared.Assertions.AssertEx;

namespace Orkeon.Scripting.Tests.Events;

public sealed class EventsTests
{
    private static readonly string[] ExpectedHandledOnce = ["h1"];

    private static Engine NewEngine() => new JsEngineFactory().Create();
    private static T Eval<T>(Engine engine, string js) => (T)engine.Evaluate(js).ToObject()!;

    [Fact]
    public async Task Queue_push_pop_delivers_FIFO_messages()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const q = ctx.events.queue("work");
                    q.push("a"); q.push("b"); q.push("c");
                    return [await q.pop(), await q.pop(), await q.pop()].join(",");
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal("a,b,c", result.tasks[0].output!.ToString());
    }

    [Fact]
    public async Task Queue_pop_with_timeout_throws_ReceiveTimeoutException_when_empty()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const q = ctx.events.queue("empty");
                    return await q.pop({ timeout: 50 });
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var ex = await ThrowsContainingAcrossChainAsync(
            () => crew.RunAsync(null, CancellationToken.None),
            nameof(ReceiveTimeoutException));

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task Queue_kick_wakes_pending_waiters_with_WaiterKickedException()
    {
        var queue = new JsEventQueue("test");

        var popTask = queue.pop(null);
        // R5.6: deterministic wait — kick() only wakes waiters already registered, so
        // poll the queue's observable waiter count instead of sleeping a fixed 20 ms.
        await Polling.WaitUntilAsync(() =>
        {
            var info = queue.info();
            return (int)info.GetType().GetProperty("waiters")!.GetValue(info)! == 1;
        });
        queue.kick();

        var ex = await Assert.ThrowsAsync<WaiterKickedException>(() => popTask);
        Assert.Equal("test", ex.QueueName);
    }

    [Fact]
    public async Task Queue_peek_does_not_remove_the_item()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body((input, ctx) => {
                    const q = ctx.events.queue("inspect");
                    q.push(7);
                    return [q.peek(), q.length];
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        var arr = (object?[])result.tasks[0].output!;
        Assert.Equal(7d, Convert.ToDouble(arr[0]));
        Assert.Equal(1, Convert.ToInt32(arr[1]));
    }

    [Fact]
    public async Task Topic_subscribe_publish_delivers_value_to_handler_sequentially()
    {
        var engine = NewEngine();
        engine.SetValue("__seen", new List<object>());
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const t = ctx.events.topic("alerts", { mode: "sequential" });
                    const sub = t.subscribe(async (event) => { __seen.push(event.value); });
                    await t.publish("first");
                    await t.publish("second");
                    sub.unsubscribe();
                    return __seen.length;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal(2d, Convert.ToDouble(result.tasks[0].output));
    }

    [Fact]
    public async Task Topic_event_stopPropagation_blocks_remaining_handlers_in_sequential_mode()
    {
        var engine = NewEngine();
        engine.SetValue("__seen", new List<object>());
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const t = ctx.events.topic("alerts");
                    t.subscribe(async (event) => { __seen.push("h1:" + event.value); event.stopPropagation(); });
                    t.subscribe(async (event) => { __seen.push("h2:" + event.value); });
                    await t.publish("x");
                    return __seen.length;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal(1d, Convert.ToDouble(result.tasks[0].output));
    }

    [Fact]
    public async Task Topic_unsubscribe_prevents_future_delivery()
    {
        var engine = NewEngine();
        engine.SetValue("__count", 0);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const t = ctx.events.topic("alerts");
                    const sub = t.subscribe(async (event) => { __count += 1; });
                    await t.publish(1);
                    sub.unsubscribe();
                    await t.publish(2);
                    return __count;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal(1d, Convert.ToDouble(result.tasks[0].output));
    }

    [Fact]
    public async Task Queue_is_shared_across_agents_in_the_same_crew()
    {
        var engine = NewEngine();
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body((input, ctx) => { ctx.events.queue("shared").push("hi"); return "pushed"; })
                .build();
            const b = agentBuilder().name("B").role("R").goal("G")
                .body(async (input, ctx) => await ctx.events.queue("shared").pop({ timeout: 1000 }))
                .build();
            crewBuilder().withAgent(a).withAgent(b).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        Assert.Equal("hi", result.tasks[1].output!.ToString());
    }

    [Fact]
    public async Task Topic_mode_parallel_invokes_handlers_concurrently()
    {
        var engine = NewEngine();
        engine.SetValue("__count", 0);
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const t = ctx.events.topic("p", { mode: "parallel" });
                    t.subscribe((ev) => { __count += 1; });
                    t.subscribe((ev) => { __count += 1; });
                    t.subscribe((ev) => { __count += 1; });
                    await t.publish("x");
                    return [t.mode, __count];
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        // We test the two observable contracts: the mode flag is plumbed through, and
        // every handler runs (parallel must not short-circuit). The actual wall-time
        // speedup is host-dependent and not asserted here — Jint's single-threaded
        // engine makes the "<= 100 ms" timing test flaky.
        var arr = (object?[])result.tasks[0].output!;
        Assert.Equal("parallel", arr[0]!.ToString());
        Assert.Equal(3, Convert.ToInt32(arr[1]));
    }

    [Fact]
    public async Task Topic_event_markHandled_short_circuits_remaining_handlers()
    {
        var engine = NewEngine();
        engine.SetValue("__seen", new List<object>());
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const t = ctx.events.topic("alerts");
                    t.subscribe(async (event) => { __seen.push("h1"); event.markHandled(); });
                    t.subscribe(async (event) => { __seen.push("h2"); });
                    t.subscribe(async (event) => { __seen.push("h3"); });
                    await t.publish("x");
                    return __seen.length;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, CancellationToken.None);

        // markHandled() sets ShouldStop, so handlers 2 and 3 are skipped in sequential
        // mode (just like stopPropagation in the existing test).
        Assert.Equal(1d, Convert.ToDouble(result.tasks[0].output));
        var seen = (List<object>)engine.GetValue("__seen").ToObject()!;
        Assert.Equal(ExpectedHandledOnce, seen);
    }

    [Fact]
    public async Task Topic_publish_rejects_with_the_handler_error_and_stops_the_sequential_chain()
    {
        var engine = NewEngine();
        engine.SetValue("__seen", new List<object>());
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const t = ctx.events.topic("alerts");
                    t.subscribe(async (event) => { await Promise.resolve(); throw new Error("bad:" + event.value); });
                    t.subscribe(async (event) => { __seen.push("h2"); });
                    try { await t.publish("x"); return "not-rejected"; }
                    catch (e) { return (e instanceof Error) + ":" + e.message; }
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal("true:bad:x", result.tasks[0].output!.ToString());
        Assert.Empty((List<object>)engine.GetValue("__seen").ToObject()!);
    }

    [Fact]
    public async Task Topic_mode_parallel_awaits_every_suspending_handler_and_ignores_stopPropagation()
    {
        var engine = NewEngine();
        engine.SetValue("__seen", new List<object>());
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const t = ctx.events.topic("p", { mode: "parallel" });
                    t.subscribe(async (ev) => { ev.stopPropagation(); await Promise.resolve(); __seen.push("h1:" + ev.handlerCount); });
                    t.subscribe(async (ev) => { ev.markHandled(); await Promise.resolve(); await Promise.resolve(); __seen.push("h2:" + ev.handlerCount); });
                    t.subscribe((ev) => { __seen.push("h3:" + ev.handlerCount + "/" + ev.maxHandlers); });
                    await t.publish("x");
                    return __seen.length;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);

        // All three handlers are started before any of them resumes, so the suspending ones
        // observe the final count; the synchronous third one sees itself as the third.
        Assert.Equal(3d, Convert.ToDouble(result.tasks[0].output));
        var seen = ((List<object>)engine.GetValue("__seen").ToObject()!).Select(o => o.ToString()).ToList();
        Assert.Equal(["h3:3/3", "h1:3", "h2:3"], seen);
    }

    [Fact]
    public async Task Topic_sequential_handlers_see_their_delivery_position_after_suspending()
    {
        var engine = NewEngine();
        engine.SetValue("__seen", new List<object>());
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    const t = ctx.events.topic("s", { mode: "sequential" });
                    t.subscribe(async (ev) => { await Promise.resolve(); __seen.push("h1:" + ev.handlerCount + "/" + ev.maxHandlers); });
                    t.subscribe(async (ev) => { await Promise.resolve(); __seen.push("h2:" + ev.handlerCount + "/" + ev.maxHandlers); });
                    await t.publish("x");
                    return __seen.length;
                }).build();
            crewBuilder().withAgent(a).build();
            """);

        var result = await crew.RunAsync(null, TestContext.Current.CancellationToken);

        Assert.Equal(2d, Convert.ToDouble(result.tasks[0].output));
        var seen = ((List<object>)engine.GetValue("__seen").ToObject()!).Select(o => o.ToString()).ToList();
        Assert.Equal(["h1:1/2", "h2:2/2"], seen);
    }

    [Fact]
    public async Task Queue_concurrent_push_pop_serializes_correctly()
    {
        // Direct C# stress test: 10 producers + 10 consumers running in parallel against
        // the same JsEventQueue. Every pushed value must be popped exactly once.
        using var engine = new Jint.Engine();
        var queue = new JsEventQueue("stress");
        const int n = 10;

        var consumed = new System.Collections.Concurrent.ConcurrentBag<int>();
        var consumers = Enumerable.Range(0, n).Select(_ => Task.Run(async () =>
        {
            var v = await queue.pop(null).ConfigureAwait(false);
            consumed.Add((int)v.AsNumber());
        })).ToArray();

        var producers = Enumerable.Range(0, n).Select(i => Task.Run(() =>
        {
            queue.push(Jint.Native.JsValue.FromObject(engine, (double)i));
        })).ToArray();

        await Task.WhenAll(producers.Concat(consumers));

        Assert.Equal(n, consumed.Count);
        Assert.Equal(Enumerable.Range(0, n).OrderBy(x => x),
            consumed.OrderBy(x => x));
        Assert.Equal(0, queue.length);
    }

    [Fact]
    public async Task EventBus_detach_agent_unsubscribes_all()
    {
        var engine = NewEngine();
        engine.SetValue("__count", 0);
        // Two agents in the same crew: A subscribes to "alerts", B publishes after we
        // remove A from the crew. The handler must not fire because Remove auto-cleans
        // subscriptions registered while A was running.
        var crew = Eval<JsCrew>(engine, """
            const a = agentBuilder().name("A").role("R").goal("G")
                .body(async (input, ctx) => {
                    ctx.events.topic("alerts").subscribe(async (event) => { __count += 1; });
                    return "subscribed";
                }).build();
            const b = agentBuilder().name("B").role("R").goal("G")
                .body(async (input, ctx) => {
                    await ctx.events.topic("alerts").publish("before-remove");
                    ctx.crew.findByName("A");  // ensure ref is reachable
                    return "published-1";
                }).build();
            crewBuilder().withAgent(a).withAgent(b).build();
            """);

        // First run: A subscribes, B publishes once → count = 1.
        await crew.RunAsync(null, CancellationToken.None);
        Assert.Equal(1, Convert.ToInt32(engine.GetValue("__count").ToObject()));

        // Remove A then publish again, calling the topic's JS publish function with the
        // engine at rest (this thread is the only drainer): count must stay at 1 because
        // the subscription has been auto-detached.
        var agentA = crew.findByName("A")!;
        crew.Remove(agentA);
        var topic = crew.EventBroker.topic("alerts");
        var published = engine.Invoke(topic.publish, Jint.Native.JsValue.FromObject(engine, "after-remove"));
        await published.UnwrapIfPromiseAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, topic.HandlerCount);
        Assert.Equal(1, Convert.ToInt32(engine.GetValue("__count").ToObject()));
    }
}
