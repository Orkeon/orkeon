using Jint.Runtime;
using Orkeon.Scripting.Configuration;

namespace Orkeon.Scripting.Tests;

public sealed class JintSandboxTests
{
    [Fact]
    public void Engine_aborts_long_running_loop_after_TimeoutInterval()
    {
        var factory = new JsEngineFactory(new ScriptingLimitsOptions
        {
            ExecutionTimeout = TimeSpan.FromMilliseconds(200),
        });
        var engine = factory.Create();

        Assert.Throws<TimeoutException>(() => engine.Evaluate("while (true) { }"));
    }

    [Fact]
    public void Engine_throws_when_recursion_limit_is_exceeded()
    {
        var factory = new JsEngineFactory(new ScriptingLimitsOptions
        {
            RecursionLimit = 10,
        });
        var engine = factory.Create();

        Assert.Throws<RecursionDepthOverflowException>(
            () => engine.Evaluate("function rec(n) { return rec(n + 1); } rec(0);"));
    }

    [Fact]
    public void Engine_throws_when_memory_limit_is_exceeded()
    {
        var factory = new JsEngineFactory(new ScriptingLimitsOptions
        {
            MemoryLimitBytes = 1L * 1024 * 1024,
        });
        var engine = factory.Create();

        Assert.Throws<MemoryLimitExceededException>(() => engine.Evaluate(
            "var arr = []; for (var i = 0; i < 1_000_000; i++) { arr.push('x'.repeat(1000)); }"));
    }

    // --- R2.6 / SEC-009: strict "Untrusted-by-default" sandbox profile ---

    [Fact]
    public void Default_limits_are_strict_untrusted_profile()
    {
        var defaults = new ScriptingLimitsOptions();

        // Memory: ~100 MB, not 32 GB.
        Assert.Equal(100L * 1024 * 1024, defaults.MemoryLimitBytes);
        // Timeout: tens of seconds, not 1 hour.
        Assert.Equal(TimeSpan.FromSeconds(30), defaults.ExecutionTimeout);
        // Recursion lowered below the previous 100.
        Assert.Equal(64, defaults.RecursionLimit);
    }

    [Fact]
    public void Default_profile_aborts_a_runaway_loop()
    {
        // Proves the *default* timeout (no override) interrupts a hostile busy loop.
        // 30 s is the strict default; the abort fires well before that, so this stays fast.
        var factory = new JsEngineFactory(new ScriptingLimitsOptions());
        var engine = factory.Create();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => engine.Evaluate("while (true) { }"));
        sw.Stop();

        // Sanity: the default ceiling is bounded (tens of seconds), not an hour.
        Assert.True(sw.Elapsed <= TimeSpan.FromSeconds(45),
            $"runaway loop should be aborted by the strict default timeout, took {sw.Elapsed}");
    }

    [Fact]
    public void Default_profile_interrupts_memory_runaway()
    {
        // Proves the *default* memory ceiling (no override) interrupts a script that
        // allocates without bound, under the strict 100 MB default.
        var factory = new JsEngineFactory(new ScriptingLimitsOptions());
        var engine = factory.Create();

        Assert.Throws<MemoryLimitExceededException>(() => engine.Evaluate(
            "var arr = []; for (var i = 0; i < 100_000_000; i++) { arr.push('x'.repeat(10000)); }"));
    }

    [Fact]
    public void Engine_has_no_CLR_access_so_scripts_cannot_reach_Process()
    {
        // The CLR boundary stays closed: AllowClr() is never called anywhere in the
        // codebase, so `System` / `importNamespace` are undefined and a script cannot
        // reach System.Diagnostics.Process to spawn a native process.
        var factory = new JsEngineFactory(new ScriptingLimitsOptions());
        var engine = factory.Create();

        Assert.Equal(true, engine.Evaluate("typeof importNamespace === 'undefined'").ToObject());
        Assert.Equal(true, engine.Evaluate("typeof System === 'undefined'").ToObject());

        // Attempting to use the CLR-interop entry point throws (it does not exist).
        Assert.ThrowsAny<Exception>(() => engine.Evaluate(
            "var p = importNamespace('System.Diagnostics'); p.Process.Start('echo');"));
    }
}
