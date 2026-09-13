using Jint;
using Jint.Native;
using Jint.Runtime;
using Orkeon.Scripting.Internal;

namespace Orkeon.Scripting.Tests.Internal;

/// <summary>
/// The contract the JS trampolines rely on (SCR-25): a CLR failure raised by a helper called
/// from JavaScript is a JavaScript throw — <c>catch</c> and <c>finally</c> run, the promise
/// rejects — and the CLR side gets its typed exception back from the rejection.
/// </summary>
public sealed class JsHostErrorTests
{
    private sealed class TypedException(string message) : Exception(message);

    private static Engine NewEngine() => new JsEngineFactory().Create();

    /// <summary>
    /// The engine at rest, drained from the test thread: the only drainer, the shape of every
    /// root pump. The 5 s ceiling turns a never-settling promise into a failure.
    /// </summary>
    private static Task<JsValue> RunAsync(Engine engine, string source)
        => engine.EvaluateAsync(source, cancellationToken: TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

    [Fact]
    public async Task A_bridged_exception_is_caught_by_the_script_and_runs_its_finally()
    {
        var engine = NewEngine();
        var log = new List<string>();
        engine.SetValue("note", new Action<string>(log.Add));
        engine.SetValue("boom", new Action(() => throw JsHostError.Wrap(engine, new TypedException("typed-boom"))));

        var result = await RunAsync(engine, """
            (async () => {
              try { await Promise.resolve(); boom(); return 'unreachable'; }
              catch (e) { note('caught:' + e.message + ':' + (e instanceof Error) + ':' + e.clrType); return 'recovered'; }
              finally { note('finally'); }
            })()
            """);

        Assert.Equal("recovered", result.AsString());
        Assert.Equal(["caught:typed-boom:true:TypedException", "finally"], log);
    }

    [Fact]
    public async Task A_bridged_exception_that_escapes_the_script_comes_back_typed()
    {
        var engine = NewEngine();
        engine.SetValue("boom", new Action(() => throw JsHostError.Wrap(engine, new TypedException("typed-boom"))));

        var rejected = await Assert.ThrowsAsync<PromiseRejectedException>(
            () => RunAsync(engine, "(async () => { await Promise.resolve(); boom(); })()"));

        var typed = Assert.IsType<TypedException>(JsHostError.Unwrap(rejected.RejectedValue));
        Assert.Equal("typed-boom", typed.Message);
    }

    [Fact]
    public void Guard_bridges_what_the_body_throws_and_passes_a_javascript_throw_through()
    {
        var engine = NewEngine();
        var js = new JavaScriptException(engine.Evaluate("new Error('mine')"));

        var bridged = Assert.Throws<JavaScriptException>(() => JsHostError.Guard(engine, () => throw new TypedException("x")));
        var passed = Assert.Throws<JavaScriptException>(() => JsHostError.Guard(engine, () => throw js));

        Assert.IsType<TypedException>(JsHostError.Unwrap(bridged.Error));
        Assert.Same(js, passed);
        Assert.Equal(42, JsHostError.Guard(engine, () => 42));
    }

    [Fact]
    public async Task A_faulted_task_awaited_by_the_script_unwraps_to_its_aggregate()
    {
        var engine = NewEngine();
        engine.SetValue("boom", new Func<Task<object?>>(() => Task.FromException<object?>(new TypedException("task-boom"))));

        var rejected = await Assert.ThrowsAsync<PromiseRejectedException>(
            () => RunAsync(engine, "(async () => { await boom(); })()"));

        var aggregate = Assert.IsType<AggregateException>(JsHostError.Unwrap(rejected.RejectedValue));
        Assert.IsType<TypedException>(aggregate.InnerExceptions[0]);
    }

    [Fact]
    public async Task A_plain_script_error_unwraps_to_nothing()
    {
        var engine = NewEngine();

        var rejected = await Assert.ThrowsAsync<PromiseRejectedException>(
            () => RunAsync(engine, "(async () => { throw new Error('mine'); })()"));

        Assert.Null(JsHostError.Unwrap(rejected.RejectedValue));
        Assert.Null(JsHostError.Unwrap(JsValue.Undefined));
        Assert.Null(JsHostError.Unwrap(null));
    }
}
