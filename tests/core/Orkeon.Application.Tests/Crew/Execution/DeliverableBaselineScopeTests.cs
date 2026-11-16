using Orkeon.Application.Crew.Execution;
using Orkeon.Application.Tests.Doubles;
using Orkeon.Domain.Common;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// #392: the state of a deliverable before a task is read once per task and run, and that read belongs to
/// no single attempt: a ballot cancelled while it waits never faults the others, nor the cached state.
/// </summary>
public sealed class DeliverableBaselineScopeTests
{
    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldHandTheStateToTheOtherAttempts_WhenOneIsCancelledWhileItWaits()
    {
        var read = new FakeDeliverableBaselineRead();
        var taskId = TaskId.Create();
        using var cancelled = new CancellationTokenSource();
        using (DeliverableBaselineScope.Begin())
        {
            var first = DeliverableBaselineScope.FirstAttemptAsync<string>(taskId, read.ReadAsync, cancelled.Token);
            var second = DeliverableBaselineScope.FirstAttemptAsync<string>(taskId, read.ReadAsync, TestContext.Current.CancellationToken);

            await cancelled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            read.Gates[0].SetResult("before");

            Assert.Equal("before", await second);
            Assert.Equal("before", await DeliverableBaselineScope.FirstAttemptAsync<string>(taskId, read.ReadAsync, TestContext.Current.CancellationToken));
        }

        Assert.Equal(1, read.Calls);
        Assert.False(read.Tokens[0].CanBeCanceled);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldReadAgain_WhenTheSharedReadItselfIsCutShort()
    {
        var read = new FakeDeliverableBaselineRead();
        var taskId = TaskId.Create();
        using (DeliverableBaselineScope.Begin())
        {
            var first = DeliverableBaselineScope.FirstAttemptAsync<string>(taskId, read.ReadAsync, TestContext.Current.CancellationToken);
            read.Gates[0].SetCanceled(TestContext.Current.CancellationToken);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

            var second = DeliverableBaselineScope.FirstAttemptAsync<string>(taskId, read.ReadAsync, TestContext.Current.CancellationToken);
            read.Gates[1].SetResult("after");

            Assert.Equal("after", await second);
        }

        Assert.Equal(2, read.Calls);
    }

    [Fact]
    [Trait("BR", "CH-392")]
    public async System.Threading.Tasks.Task ShouldReadWithTheAttemptsOwnToken_OutsideARun()
    {
        var read = new FakeDeliverableBaselineRead();
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        var state = DeliverableBaselineScope.FirstAttemptAsync<string>(TaskId.Create(), read.ReadAsync, attempt.Token);
        read.Gates[0].SetResult("own");

        Assert.Equal("own", await state);
        Assert.Equal(attempt.Token, read.Tokens[0]);
    }
}
