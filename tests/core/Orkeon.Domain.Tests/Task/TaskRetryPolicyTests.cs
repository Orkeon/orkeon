using Orkeon.Domain.Task;

namespace Orkeon.Domain.Tests.Task;

/// <summary>
/// FWK-15: the value object a task's <c>retry:</c> becomes. At least one attempt, a wait that is never
/// negative, and the wait before each new attempt computed, never slept, here.
/// </summary>
public sealed class TaskRetryPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [Trait("BR", "FWK-15")]
    public void ShouldRefuseAttemptsBelowOne(int attempts)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new TaskRetryPolicy(attempts));

        Assert.Contains("attempts", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public void ShouldRefuseANegativeBackoff()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new TaskRetryPolicy(3, backoffMs: -1));

        Assert.Contains("backoffMs", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public void ShouldDefaultToNoWaitAndAFixedBackoff()
    {
        var policy = new TaskRetryPolicy(3);

        Assert.Equal(3, policy.Attempts);
        Assert.Equal(0, policy.BackoffMs);
        Assert.Equal(TaskRetryBackoff.Fixed, policy.Backoff);
        Assert.Equal(TimeSpan.Zero, policy.DelayBefore(2));
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public void ShouldWaitTheSameBeforeEveryRetry_WhenTheBackoffIsFixed()
    {
        var policy = new TaskRetryPolicy(4, 100, TaskRetryBackoff.Fixed);

        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.DelayBefore(2));
        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.DelayBefore(3));
        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.DelayBefore(4));
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public void ShouldDoubleTheWaitBeforeEachRetry_WhenTheBackoffIsExponential()
    {
        var policy = new TaskRetryPolicy(4, 100, TaskRetryBackoff.Exponential);

        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.DelayBefore(2));
        Assert.Equal(TimeSpan.FromMilliseconds(200), policy.DelayBefore(3));
        Assert.Equal(TimeSpan.FromMilliseconds(400), policy.DelayBefore(4));
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public void ShouldNotWaitBeforeTheFirstAttempt()
    {
        var policy = new TaskRetryPolicy(3, 100, TaskRetryBackoff.Exponential);

        Assert.Equal(TimeSpan.Zero, policy.DelayBefore(1));
    }

    [Fact]
    [Trait("BR", "FWK-15")]
    public void ShouldCapAnExponentialWaitAtFiveMinutes()
    {
        var policy = new TaskRetryPolicy(60, 1000, TaskRetryBackoff.Exponential);

        Assert.Equal(TimeSpan.FromMinutes(5), policy.DelayBefore(60));
    }
}
