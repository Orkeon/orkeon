using System.Collections.Concurrent;
using Orkeon.Domain.Execution;
using Orkeon.Tests.Shared.Doubles;

namespace Orkeon.Domain.Tests.Execution;

/// <summary>
/// The ambient scope of one agent run: resources released at its end in reverse order and never
/// throwing, nesting that restores the caller's scope, isolation across concurrent flows, and no
/// resource accepted once the run ended.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
    Justification = "Ownership of every resource and scope created here passes to the scope under test, whose end is what the tests assert.")]
public sealed class AgentRunScopeTests
{
    [Fact]
    public void ShouldBeNull_WhenNoRunBegan()
    {
        Assert.Null(AgentRunScope.Current);
    }

    [Fact]
    [Trait("BR", "RT-06")]
    public async System.Threading.Tasks.Task ShouldCarryRootWithoutBecomingCurrent_WhenCreatedWithRoot()
    {
        var scope = AgentRunScope.Create("agent-1", "Writer", "/workspace/wt/a");

        Assert.Equal("/workspace/wt/a", scope.Root);
        Assert.Null(AgentRunScope.Current);
        using (scope.Enter())
            Assert.Equal("/workspace/wt/a", AgentRunScope.Current?.Root);
        await scope.DisposeAsync();
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDisposeRegisteredResource_WhenScopeEnds()
    {
        var resource = new FakeAsyncDisposable();

        await using (var scope = AgentRunScope.Begin("agent-1", "Writer"))
        {
            Assert.Same(scope, AgentRunScope.Current);
            Assert.Equal("agent-1", scope.AgentId);
            Assert.Equal("Writer", scope.AgentRole);
            Assert.False(string.IsNullOrEmpty(scope.RunId));
            AgentRunScope.Current!.Register(resource);
            Assert.False(resource.IsDisposed);
        }

        Assert.Equal(1, resource.DisposeCount);
        Assert.Null(AgentRunScope.Current);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldDisposeResourcesInReverseOrder_WhenScopeEnds()
    {
        var log = new ConcurrentQueue<string>();
        var scope = AgentRunScope.Create();
        scope.Register(new FakeAsyncDisposable { Name = "first", DisposalLog = log });
        scope.GetOrAdd("second", () => new FakeAsyncDisposable { Name = "second", DisposalLog = log });
        scope.Register(new FakeAsyncDisposable { Name = "third", DisposalLog = log });

        await scope.EndAsync();

        Assert.Equal(["third", "second", "first"], log.ToArray());
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReleaseOthersAndNotThrow_WhenOneDisposalFails()
    {
        var failure = new InvalidOperationException("boom");
        var before = new FakeAsyncDisposable();
        var failing = new FakeAsyncDisposable { ThrowOnDispose = failure };
        var after = new FakeAsyncDisposable();
        var scope = AgentRunScope.Create();
        scope.Register(before);
        scope.Register(failing);
        scope.Register(after);

        await scope.EndAsync();

        Assert.True(before.IsDisposed);
        Assert.True(failing.IsDisposed);
        Assert.True(after.IsDisposed);
        Assert.Same(failure, Assert.Single(scope.DisposalErrors));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldEndOnce_WhenEndedTwice()
    {
        var resource = new FakeAsyncDisposable();
        var scope = AgentRunScope.Create();
        scope.Register(resource);

        await scope.EndAsync();
        await scope.DisposeAsync();

        Assert.True(scope.IsEnded);
        Assert.Equal(1, resource.DisposeCount);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRestoreParent_WhenNestedRunEnds()
    {
        await using var outer = AgentRunScope.Begin("caller");
        var outerResource = new FakeAsyncDisposable();
        outer.Register(outerResource);

        await using (var inner = AgentRunScope.Begin("coworker"))
        {
            Assert.Same(inner, AgentRunScope.Current);
            Assert.Same(outer, inner.Parent);
            Assert.NotEqual(outer.RunId, inner.RunId);
        }

        Assert.Same(outer, AgentRunScope.Current);
        Assert.False(outer.IsEnded);
        Assert.False(outerResource.IsDisposed);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldRestorePrevious_WhenEnteredHandleIsDisposed()
    {
        await using var outer = AgentRunScope.Begin();
        var owned = AgentRunScope.Create();

        using (owned.Enter())
            Assert.Same(owned, AgentRunScope.Current);

        Assert.Same(outer, AgentRunScope.Current);
        await owned.EndAsync();
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnSameInstance_WhenGetOrAddCalledTwiceInOneScope()
    {
        await using var scope = AgentRunScope.Begin();

        var first = scope.GetOrAdd("shell", () => new FakeAsyncDisposable());
        var second = scope.GetOrAdd("shell", () => new FakeAsyncDisposable());

        Assert.Same(first, second);
        Assert.True(scope.TryGet<FakeAsyncDisposable>("shell", out var found));
        Assert.Same(first, found);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldReturnSameInstance_WhenToolsOfOneAgentRunConcurrently()
    {
        await using var scope = AgentRunScope.Begin();
        var created = 0;

        var results = await System.Threading.Tasks.Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            System.Threading.Tasks.Task.Run(() => AgentRunScope.Current!.GetOrAdd("shell", () =>
            {
                Interlocked.Increment(ref created);
                return new FakeAsyncDisposable();
            }), TestContext.Current.CancellationToken)));

        Assert.Equal(1, created);
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldGiveEachConcurrentRunItsOwnResource_WhenTwoAgentsRunInParallel()
    {
        using var bothStarted = new Barrier(2);

        async System.Threading.Tasks.Task<(AgentRunScope Scope, FakeAsyncDisposable Resource)> RunAgentAsync(string id)
        {
            await System.Threading.Tasks.Task.Yield();
            await using var scope = AgentRunScope.Begin(id);
            var resource = AgentRunScope.Current!.GetOrAdd("shell", () => new FakeAsyncDisposable());
            // Both runs hold their scope at the same time before either reads it again.
            bothStarted.SignalAndWait(TestContext.Current.CancellationToken);
            Assert.Same(scope, AgentRunScope.Current);
            Assert.Same(resource, AgentRunScope.Current!.GetOrAdd("shell", () => new FakeAsyncDisposable()));
            return (scope, resource);
        }

        var runs = await System.Threading.Tasks.Task.WhenAll(
            System.Threading.Tasks.Task.Run(() => RunAgentAsync("a"), TestContext.Current.CancellationToken),
            System.Threading.Tasks.Task.Run(() => RunAgentAsync("b"), TestContext.Current.CancellationToken));

        Assert.NotSame(runs[0].Scope, runs[1].Scope);
        Assert.NotSame(runs[0].Resource, runs[1].Resource);
        Assert.True(runs[0].Resource.IsDisposed);
        Assert.True(runs[1].Resource.IsDisposed);
        Assert.Null(AgentRunScope.Current);
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowObjectDisposedException_WhenResourceAddedAfterEnd()
    {
        var scope = AgentRunScope.Create();
        await scope.EndAsync();

        Assert.Throws<ObjectDisposedException>(() => scope.GetOrAdd("shell", () => new FakeAsyncDisposable()));
        Assert.Throws<ObjectDisposedException>(() => scope.Register(new FakeAsyncDisposable()));
    }

    [Fact]
    public async System.Threading.Tasks.Task ShouldThrowInvalidOperationException_WhenKeyHoldsAnotherType()
    {
        await using var scope = AgentRunScope.Begin();
        scope.GetOrAdd("shell", () => new FakeAsyncDisposable());

        Assert.Throws<InvalidOperationException>(() => scope.GetOrAdd("shell", () => AgentRunScope.Create()));
    }
}
