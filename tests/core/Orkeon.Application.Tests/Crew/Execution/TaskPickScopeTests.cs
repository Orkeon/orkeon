using Orkeon.Application.Crew.Execution;

namespace Orkeon.Application.Tests.Crew.Execution;

/// <summary>
/// #434: what System One picks for a task of an agent (its profile on auto, its skills) is picked once per run and
/// agent; the next run, or another agent on the task, picks its own.
/// </summary>
public sealed class TaskPickScopeTests
{
    private int _picks;

    private System.Threading.Tasks.Task<string> PickAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return System.Threading.Tasks.Task.FromResult($"pick {Interlocked.Increment(ref _picks)}");
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldHandTheFirstPickToALaterAttempt_WhenTheSameAgentRunsTheTaskAgain()
    {
        // Arrange
        using var run = TaskPickScope.Begin();
        await TaskPickScope.Current!.OnceAsync("route", "t1", "a1", PickAsync, TestContext.Current.CancellationToken);

        // Act
        var again = await TaskPickScope.Current!.OnceAsync("route", "t1", "a1", PickAsync, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("pick 1", again);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldPickAgain_WhenAnotherAgentRunsTheSameTask()
    {
        // Arrange
        using var run = TaskPickScope.Begin();
        await TaskPickScope.Current!.OnceAsync("route", "t1", "a1", PickAsync, TestContext.Current.CancellationToken);

        // Act
        var voter = await TaskPickScope.Current!.OnceAsync("route", "t1", "a2", PickAsync, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("pick 2", voter);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldPickAgain_WhenTheNextRunOpensItsScope()
    {
        // Arrange
        using (TaskPickScope.Begin())
            await TaskPickScope.Current!.OnceAsync("route", "t1", "a1", PickAsync, TestContext.Current.CancellationToken);
        using var next = TaskPickScope.Begin();

        // Act
        var picked = await TaskPickScope.Current!.OnceAsync("route", "t1", "a1", PickAsync, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("pick 2", picked);
    }

    [Fact]
    [Trait("BR", "CH-434")]
    public async System.Threading.Tasks.Task ShouldPickAgain_WhenTheFirstPickFailed()
    {
        // Arrange
        using var run = TaskPickScope.Begin();
        await Record.ExceptionAsync(() => TaskPickScope.Current!.OnceAsync<string>(
            "route", "t1", "a1", _ => throw new InvalidOperationException("down"), TestContext.Current.CancellationToken));

        // Act
        var picked = await TaskPickScope.Current!.OnceAsync("route", "t1", "a1", PickAsync, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("pick 1", picked);
    }
}
