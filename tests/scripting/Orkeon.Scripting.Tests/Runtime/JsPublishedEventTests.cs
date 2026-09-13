using System.Collections.Concurrent;
using Jint;
using Jint.Native;
using Jint.Runtime;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Runtime;

public sealed class JsPublishedEventTests
{
    private static Engine NewEngine() => new JsEngineFactory().Create();

    private static JsPublishedEvent NewEvent(Engine engine, JsValue value, int maxHandlers = 3,
        ConcurrentDictionary<string, SemaphoreSlim>? locks = null)
        => new(engine, value, maxHandlers, locks ?? new ConcurrentDictionary<string, SemaphoreSlim>());

    [Fact]
    public void Constructor_exposes_value_and_maxHandlers()
    {
        var engine = NewEngine();
        var value = JsValue.FromObject(engine, "payload");
        var evt = NewEvent(engine, value, maxHandlers: 5);

        Assert.Equal("payload", evt.value.AsString());
        Assert.Equal(5, evt.maxHandlers);
        Assert.Equal(0, evt.handlerCount);
    }

    [Fact]
    public void ShouldStop_is_false_initially()
    {
        var engine = NewEngine();
        var evt = NewEvent(engine, JsValue.Undefined);

        Assert.False(evt.ShouldStop);
    }

    [Fact]
    public void markHandled_sets_ShouldStop()
    {
        var engine = NewEngine();
        var evt = NewEvent(engine, JsValue.Undefined);

        evt.markHandled();

        Assert.True(evt.ShouldStop);
    }

    [Fact]
    public void stopPropagation_sets_ShouldStop()
    {
        var engine = NewEngine();
        var evt = NewEvent(engine, JsValue.Undefined);

        evt.stopPropagation();

        Assert.True(evt.ShouldStop);
    }

    [Fact]
    public void handlerCount_is_settable_internally()
    {
        var engine = NewEngine();
        var evt = NewEvent(engine, JsValue.Undefined);

        evt.handlerCount = 2;

        Assert.Equal(2, evt.handlerCount);
    }

    /// <summary>
    /// Calls the event's JS <c>lock</c> function the way a handler does, with the engine at
    /// rest so the test thread is the only drainer.
    /// </summary>
    private static Task<JsValue> LockAsync(Engine engine, JsPublishedEvent evt, string name, JsValue fn)
        => engine.Invoke(evt.@lock, name, fn).UnwrapIfPromiseAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task lock_runs_synchronous_function_under_named_lock()
    {
        var engine = NewEngine();
        var evt = NewEvent(engine, JsValue.Undefined);
        var fn = await engine.EvaluateAsync("() => 7", cancellationToken: TestContext.Current.CancellationToken);

        var result = await LockAsync(engine, evt, "region", fn);

        Assert.Equal(7d, result.AsNumber());
    }

    [Fact]
    public async Task lock_unwraps_promise_returning_function()
    {
        var engine = NewEngine();
        var evt = NewEvent(engine, JsValue.Undefined);
        var fn = await engine.EvaluateAsync("async () => 'done'", cancellationToken: TestContext.Current.CancellationToken);

        var result = await LockAsync(engine, evt, "region", fn);

        Assert.Equal("done", result.AsString());
    }

    [Fact]
    public async Task lock_reuses_shared_semaphore_from_dictionary()
    {
        var engine = NewEngine();
        var locks = new ConcurrentDictionary<string, SemaphoreSlim>();
        var evt = NewEvent(engine, JsValue.Undefined, locks: locks);
        var fn = await engine.EvaluateAsync("() => 1", cancellationToken: TestContext.Current.CancellationToken);

        await LockAsync(engine, evt, "shared", fn);

        Assert.True(locks.ContainsKey("shared"));
        // Lock was released, so the semaphore can be acquired again immediately.
        Assert.True(await locks["shared"].WaitAsync(0, TestContext.Current.CancellationToken));
        locks["shared"].Release();
    }

    [Fact]
    public async Task lock_releases_the_semaphore_when_the_function_throws()
    {
        var engine = NewEngine();
        var locks = new ConcurrentDictionary<string, SemaphoreSlim>();
        var evt = NewEvent(engine, JsValue.Undefined, locks: locks);
        var throwing = await engine.EvaluateAsync("async () => { throw new Error('boom'); }", cancellationToken: TestContext.Current.CancellationToken);
        var fn = await engine.EvaluateAsync("() => 'after'", cancellationToken: TestContext.Current.CancellationToken);

        var rejected = await Assert.ThrowsAsync<PromiseRejectedException>(() => LockAsync(engine, evt, "res", throwing));
        var result = await LockAsync(engine, evt, "res", fn);

        Assert.Equal("boom", rejected.RejectedValue.AsObject().Get("message").AsString());
        Assert.Equal("after", result.AsString());
    }

    [Fact]
    public async Task lock_serializes_suspending_sections_across_events_of_the_same_topic()
    {
        // Two events minted by one topic share its lock dictionary: their sections on the
        // same name run one after the other even though each suspends inside.
        var engine = NewEngine();
        var locks = new ConcurrentDictionary<string, SemaphoreSlim>();
        var first = NewEvent(engine, JsValue.Undefined, locks: locks);
        var second = NewEvent(engine, JsValue.Undefined, locks: locks);
        engine.SetValue("__log", new List<string>());
        var section = await engine.EvaluateAsync("""
            (tag) => async () => { __log.Add(tag + ':in'); await Promise.resolve(); await Promise.resolve(); __log.Add(tag + ':out'); return tag; }
            """, cancellationToken: TestContext.Current.CancellationToken);

        var both = await engine.EvaluateAsync("(a, b) => Promise.all([a, b]).then(r => r.join(','))", cancellationToken: TestContext.Current.CancellationToken);

        var a = engine.Invoke(first.@lock, "x", engine.Invoke(section, "a"));
        var b = engine.Invoke(second.@lock, "x", engine.Invoke(section, "b"));
        var results = await engine.Invoke(both, a, b).UnwrapIfPromiseAsync(TestContext.Current.CancellationToken);

        var log = (List<string>)engine.GetValue("__log").ToObject()!;
        Assert.Equal(["a:in", "a:out", "b:in", "b:out"], log);
        Assert.Equal("a,b", results.AsString());
    }
}
