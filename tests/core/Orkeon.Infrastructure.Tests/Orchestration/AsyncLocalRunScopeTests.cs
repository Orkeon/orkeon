using Orkeon.Application.Interfaces.Services;
using Orkeon.Infrastructure.Orchestration;

namespace Orkeon.Infrastructure.Tests.Orchestration;

/// <summary>FWK-01: the ambient run identity follows the control flow it was entered on.</summary>
public sealed class AsyncLocalRunScopeTests
{
    [Fact]
    [Trait("BR", "FWK-01")]
    public void ShouldRestoreTheOuterRun_WhenAnInnerRunEnds()
    {
        var scope = new AsyncLocalRunScope();
        var outer = new RunScope(ExecutionId.New(), "outer");
        var inner = new RunScope(ExecutionId.New(), "inner");

        using (scope.Enter(outer))
        {
            using (scope.Enter(inner))
                Assert.Same(inner, scope.Current);

            Assert.Same(outer, scope.Current);
        }

        Assert.Null(scope.Current);
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public async Task ShouldKeepTwoParallelRunsApart()
    {
        var scope = new AsyncLocalRunScope();
        using var bothEntered = new CountdownEvent(2);

        async Task<ExecutionId?> RunAsync(ExecutionId id)
        {
            using var _ = scope.Enter(new RunScope(id, "crew"));
            bothEntered.Signal();
            bothEntered.Wait(TestContext.Current.CancellationToken);
            await Task.Yield();
            return scope.Current?.ExecutionId;
        }

        var a = ExecutionId.New();
        var b = ExecutionId.New();
        var seen = await Task.WhenAll(Task.Run(() => RunAsync(a), TestContext.Current.CancellationToken), Task.Run(() => RunAsync(b), TestContext.Current.CancellationToken));

        Assert.Equal(a, seen[0]);
        Assert.Equal(b, seen[1]);
        Assert.Null(scope.Current);
    }

    [Fact]
    [Trait("BR", "FWK-01")]
    public void ShouldShareTheSlotBetweenInstances()
    {
        var one = new AsyncLocalRunScope();
        var another = new AsyncLocalRunScope();
        var run = new RunScope(ExecutionId.New(), "crew");

        using (one.Enter(run))
            Assert.Same(run, another.Current);
    }
}
