using Jint;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Tests.Internal;

/// <summary>
/// WHEN a trampoline factory is evaluated (SCR-25 T7; the task sheet, section 7). <c>Engine.Evaluate</c>
/// drains the queued event-loop jobs on its way out, so a factory evaluated lazily from a script's
/// synchronous prefix ran the prefix's own microtasks in the middle of a statement — a
/// <c>then</c> callback queued two lines earlier observed state the prefix had not finished
/// writing. <see cref="JsEngineFactory.Create"/> evaluates every factory with the engine at rest
/// (<see cref="JsTrampolineFactories.Prepare"/>); the first touch of a trampoline from the prefix
/// only invokes, and the microtasks run where the language puts them: after the prefix.
/// </summary>
public sealed class JsTrampolineFactoriesTests
{
    /// <summary>
    /// Each row first-touches one trampoline from the script's synchronous prefix: directly
    /// (<c>fsm.send</c>, <c>graph.run</c>, <c>crew.run</c>, a bridged throw) or through a lifecycle
    /// hook <c>build()</c> fires synchronously (<c>ctx.crew.lock</c>, <c>ctx.llm.act</c>,
    /// <c>ctx.llm.stream</c>, a topic's <c>publish</c>, a published event's <c>lock</c> from the
    /// handler <c>publish</c> calls before its first await). The agent context's own surfaces
    /// (<c>ctx.lock</c>, <c>ctx.state.with</c>) exist only inside a run's job, where a nested
    /// evaluation cannot drain; the store treats them alike anyway.
    /// </summary>
    public static TheoryData<string, string> FirstTouches => new()
    {
        { "fsm.send", """stateMachine({ name: "m", initial: "a", states: { a: {} } }).send;""" },
        { "graph.run", """stateGraph({ name: "g", nodes: { a: (s) => s }, edges: { [START]: "a", a: END } }).run;""" },
        { "crew.run", """crewBuilder().withAgent(agentBuilder().name("A").role("R").goal("G").build()).build().run;""" },
        {
            "bridged throw",
            """
            const a = agentBuilder().name("A").role("R").goal("G").build();
            const c = crewBuilder().name("c").withAgent(a).build();
            try { c.add(agentBuilder().name("A").role("R").goal("G").build()); } catch (e) { }
            """
        },
        { "ctx.crew.lock", Hook("ctx.crew.lock;") },
        { "ctx.llm.act", Hook("ctx.llm.act;") },
        { "ctx.llm.stream", Hook("ctx.llm.stream('p');") },
        { "topic.publish", Hook("ctx.events.topic('t').publish;") },
        { "event.lock", Hook("const t = ctx.events.topic('t'); t.subscribe((ev) => { ev.lock; }); t.publish(1);") },
    };

    private static string Hook(string body) =>
        $$"""crewBuilder().withAgent(agentBuilder().name("A").role("R").goal("G").onAgentStart((ctx) => { {{body}} }).build()).build();""";

    [Theory]
    [MemberData(nameof(FirstTouches))]
    public void First_touch_from_the_prefix_does_not_run_queued_microtasks_mid_statement(string touch, string js)
    {
        var order = OrderAfter(new JsEngineFactory().Create(), js);

        Assert.True(new[] { "prefix-done", "microtask" }.SequenceEqual(order),
            $"{touch}: the microtask queued by the prefix ran mid-statement, at the first touch of the trampoline: [{string.Join(", ", order)}]");
    }

    /// <summary>
    /// The mechanism on the store itself, every factory included — the ones no prefix can reach
    /// (the agent context's <c>stateWith</c> and state proxy) as well: after <see cref="JsTrampolineFactories.Prepare"/>
    /// a first use from the prefix evaluates nothing, so the prefix's microtask waits for the prefix.
    /// </summary>
    [Fact]
    public void After_Prepare_no_factory_evaluates_at_its_first_use()
    {
        using var engine = new Engine();
        JsTrampolineFactories.Prepare(engine);
        engine.SetValue("__touchAll", new Action(() =>
        {
            foreach (var factory in JsTrampolineFactories.All)
                Assert.True(factory.For(engine).IsObject());
        }));

        Assert.Equal(["prefix-done", "microtask"], OrderAfter(engine, "__touchAll();"));
    }

    /// <summary>
    /// The oracle of <see cref="JsTrampolineFactories.All"/>, the hand-maintained list <see cref="JsTrampolineFactories.Prepare"/>
    /// and the fact above iterate: every <c>Factory</c> field the class declares is in it. A twelfth
    /// trampoline added as a field but not to the list would be evaluated lazily at its first touch —
    /// the mid-statement drain this class exists to keep out — with every other test here still green.
    /// </summary>
    [Fact]
    public void Every_declared_factory_is_in_All()
    {
        var declared = typeof(JsTrampolineFactories)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(JsTrampolineFactories.Factory))
            .Select(f => (f.Name, Factory: (JsTrampolineFactories.Factory)f.GetValue(null)!))
            .ToList();

        Assert.NotEmpty(declared);
        var missing = declared.Where(d => !JsTrampolineFactories.All.Contains(d.Factory)).Select(d => d.Name).ToList();
        Assert.True(missing.Count == 0, "Factory fields missing from JsTrampolineFactories.All: " + string.Join(", ", missing));
        Assert.Equal(declared.Count, JsTrampolineFactories.All.Distinct().Count());
    }

    /// <summary>
    /// The hazard itself, pinned: on a bare engine the lazy fallback evaluates at first use, and
    /// that evaluation drains the prefix's microtask mid-statement. It is why <see cref="JsTrampolineFactories.Prepare"/>
    /// exists, and what makes the rows above meaningful — the same script, prepared, comes out the other way.
    /// </summary>
    [Fact]
    public void A_bare_engine_evaluates_at_first_use_and_that_drains_the_queue()
    {
        using var engine = new Engine();
        engine.SetValue("__touch", new Action(() => JsTrampolineFactories.Lock.For(engine)));

        Assert.Equal(["microtask", "prefix-done"], OrderAfter(engine, "__touch();"));
    }

    /// <summary>The same factory value comes back for an engine's lifetime, and another engine gets its own.</summary>
    [Fact]
    public void A_factory_is_evaluated_once_per_engine()
    {
        using var engine = new Engine();
        using var other = new Engine();

        var first = JsTrampolineFactories.HostError.For(engine);

        Assert.Same(first, JsTrampolineFactories.HostError.For(engine));
        Assert.NotSame(first, JsTrampolineFactories.HostError.For(other));
    }

    private static string[] OrderAfter(Engine engine, string touch)
    {
        engine.Evaluate($$"""
            globalThis.order = [];
            Promise.resolve().then(() => order.push("microtask"));
            {{touch}}
            order.push("prefix-done");
            """);
        return engine.GetValue("order").AsArray().Select(v => v.AsString()).ToArray();
    }
}
