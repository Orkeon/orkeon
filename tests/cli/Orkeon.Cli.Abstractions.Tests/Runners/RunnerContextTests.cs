using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.Abstractions.Tests.Fixtures;

namespace Orkeon.Cli.Abstractions.Tests.Runners;

public sealed class RunnerContextTests
{
    [Fact]
    public async Task Current_is_null_when_no_runner_active()
    {
        // No push prior — start of a fresh async flow.
        await Task.Run(() => Assert.Null(RunnerContext.Current), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Push_sets_current_to_pushed_registry()
    {
        var reg = new TestCommandRegistry();
        using var _ = RunnerContext.Push(reg);

        Assert.Same(reg, RunnerContext.Current);
    }

    [Fact]
    public void Dispose_pops_back_to_previous()
    {
        var outer = new TestCommandRegistry();
        var inner = new TestCommandRegistry();

        using var o = RunnerContext.Push(outer);
        Assert.Same(outer, RunnerContext.Current);

        using (var i = RunnerContext.Push(inner))
        {
            Assert.Same(inner, RunnerContext.Current);
        }

        Assert.Same(outer, RunnerContext.Current);
    }

    [Fact]
    public async Task Nested_push_pop_restores_correctly()
    {
        var a = new TestCommandRegistry();
        var b = new TestCommandRegistry();
        var c = new TestCommandRegistry();

        using (var _a = RunnerContext.Push(a))
        using (var _b = RunnerContext.Push(b))
        using (var _c = RunnerContext.Push(c))
        {
            Assert.Same(c, RunnerContext.Current);
        }
        // After all disposed, no leftover.
        await Task.Run(() => Assert.Null(RunnerContext.Current), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AsyncLocal_flow_across_await()
    {
        var reg = new TestCommandRegistry();
        using var _ = RunnerContext.Push(reg);

        await Task.Yield();
        Assert.Same(reg, RunnerContext.Current);

        await Task.Run(() => Assert.Same(reg, RunnerContext.Current), TestContext.Current.CancellationToken);
    }
}
