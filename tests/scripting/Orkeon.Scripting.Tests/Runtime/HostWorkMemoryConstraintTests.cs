using Jint.Runtime;
using Orkeon.Scripting.Runtime;
using Orkeon.Tests.Shared.Timing;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// The engine's memory limit as <c>JsEngineFactory</c> sets it: Jint's rule — the bytes allocated since the
/// window opened — except that a host tool's work is left out, and a move of the engine to another thread
/// keeps counting from that thread's own counter.
/// </summary>
public sealed class HostWorkMemoryConstraintTests
{
    private const long Limit = 16L * 1024 * 1024;
    private const int TenMegabytes = 10 * 1024 * 1024;

    [Fact]
    public void ShouldLeaveOutTheHostWork_WhenItRunsOnTheEnginesThread()
    {
        OnNewThread(() =>
        {
            var constraint = new HostWorkMemoryConstraint(Limit);
            constraint.Reset();

            using (constraint.ExcludeHostWork())
                for (var i = 0; i < 6; i++) Allocate(TenMegabytes);
            constraint.Check();
            Allocate(TenMegabytes);
            Allocate(TenMegabytes);

            Assert.Throws<MemoryLimitExceededException>(constraint.Check);
        });
    }

    [Fact]
    public void ShouldNotChargeTheNewThreadsPastAllocations_WhenTheEngineMovesThread()
    {
        var constraint = new HostWorkMemoryConstraint(Limit);
        OnNewThread(constraint.Reset);

        OnNewThread(() =>
        {
            for (var i = 0; i < 6; i++) Allocate(TenMegabytes);
            constraint.Check();
        });
    }

    [Fact]
    public void ShouldKeepWhatTheFirstThreadCounted_WhenTheEngineMovesThread()
    {
        var constraint = new HostWorkMemoryConstraint(Limit);
        OnNewThread(() =>
        {
            constraint.Reset();
            Allocate(TenMegabytes);
            constraint.Check();
        });

        OnNewThread(() =>
        {
            constraint.Check();
            Allocate(TenMegabytes);

            Assert.Throws<MemoryLimitExceededException>(constraint.Check);
        });
    }

    [Fact]
    [Trait("BR", "R-55")]
    public void ShouldCloseWindow_WhenHostToolThrowsInsideScope()
    {
        OnNewThread(() =>
        {
            var constraint = new HostWorkMemoryConstraint(Limit);
            constraint.Reset();

            var scope = constraint.ExcludeHostWork();
            try
            {
                for (var i = 0; i < 6; i++) Allocate(TenMegabytes);
                throw new InvalidOperationException("host tool failed");
            }
            catch (InvalidOperationException) { }
            finally { scope.Dispose(); }
            // a second end of the same scope (a cleanup path run twice) must not widen the window
            scope.Dispose();
            constraint.Check();
            Allocate(TenMegabytes);
            Allocate(TenMegabytes);

            Assert.Throws<MemoryLimitExceededException>(constraint.Check);
        });
    }

    [Fact]
    [Trait("BR", "R-55")]
    public void ShouldKeepWindowOpen_WhenScopesNestUntilOuterEnds()
    {
        OnNewThread(() =>
        {
            var constraint = new HostWorkMemoryConstraint(Limit);
            constraint.Reset();

            var outer = constraint.ExcludeHostWork();
            using (constraint.ExcludeHostWork())
                Allocate(TenMegabytes);
            for (var i = 0; i < 5; i++) Allocate(TenMegabytes);

            // the inner scope ended, the outer is still open: these bytes are not counted
            constraint.Check();
            outer.Dispose();
            constraint.Check();
        });
    }

    [Fact]
    [Trait("BR", "R-55")]
    public void ShouldKeepExclusion_WhenResetRunsInsideScope()
    {
        OnNewThread(() =>
        {
            var constraint = new HostWorkMemoryConstraint(Limit);
            constraint.Reset();

            using (constraint.ExcludeHostWork())
            {
                constraint.Reset();
                for (var i = 0; i < 6; i++) Allocate(TenMegabytes);
                constraint.Check();
            }

            constraint.Check();
            for (var i = 0; i < 6; i++) Allocate(TenMegabytes);

            Assert.Throws<MemoryLimitExceededException>(constraint.Check);
        });
    }

    [Fact]
    [Trait("BR", "R-54")]
    public void ShouldCountMemory_WhenCheckRunsOnAnotherThreadThanScope()
    {
        OnNewThread(() =>
        {
            var constraint = new HostWorkMemoryConstraint(Limit);
            constraint.Reset();

            var scope = constraint.ExcludeHostWork();
            for (var i = 0; i < 6; i++) Allocate(TenMegabytes);
            OnNewThread(constraint.Check);
            constraint.Check();
            scope.Dispose();
            Allocate(TenMegabytes);
            Allocate(TenMegabytes);

            Assert.Throws<MemoryLimitExceededException>(constraint.Check);
        });
    }

    private static void Allocate(int bytes) => GC.KeepAlive(new byte[bytes]);

    // A dedicated thread, so each step reads a counter of its own.
    private static void OnNewThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { error = e; }
        });
        thread.Start();
        Assert.True(thread.Join(HangGuards.EnginePromise), "the step did not finish");
        if (error is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
    }
}
