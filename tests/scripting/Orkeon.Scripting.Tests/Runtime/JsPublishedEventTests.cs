using System.Collections.Concurrent;
using Jint;
using Jint.Native;
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

    [Fact]
    public async Task lock_runs_synchronous_function_under_named_lock()
    {
        var engine = NewEngine();
        var evt = NewEvent(engine, JsValue.Undefined);
        var fn = await engine.EvaluateAsync("() => 7", cancellationToken: TestContext.Current.CancellationToken);

        var result = await evt.@lock("region", fn);

        Assert.Equal(7d, result.AsNumber());
    }

    [Fact]
    public async Task lock_unwraps_promise_returning_function()
    {
        var engine = NewEngine();
        var evt = NewEvent(engine, JsValue.Undefined);
        var fn = await engine.EvaluateAsync("async () => 'done'", cancellationToken: TestContext.Current.CancellationToken);

        var result = await evt.@lock("region", fn);

        Assert.Equal("done", result.AsString());
    }

    [Fact]
    public async Task lock_reuses_shared_semaphore_from_dictionary()
    {
        var engine = NewEngine();
        var locks = new ConcurrentDictionary<string, SemaphoreSlim>();
        var evt = NewEvent(engine, JsValue.Undefined, locks: locks);
        var fn = await engine.EvaluateAsync("() => 1", cancellationToken: TestContext.Current.CancellationToken);

        await evt.@lock("shared", fn);

        Assert.True(locks.ContainsKey("shared"));
        // Lock was released, so the semaphore can be acquired again immediately.
        Assert.True(await locks["shared"].WaitAsync(0, TestContext.Current.CancellationToken));
        locks["shared"].Release();
    }
}
