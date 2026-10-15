using Orkeon.Scripting.Runtime;
using Orkeon.Scripting.Tests.Doubles;

namespace Orkeon.Scripting.Tests.Runtime;

/// <summary>
/// The engine's wall-clock limit as <c>JsEngineFactory</c> sets it: Jint's rule — a window opened at
/// each reset — except that the time the permission gate holds a call, paused, is not counted.
/// </summary>
public sealed class PausableTimeConstraintTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void ShouldThrowTheTimeoutJintThrows_WhenTheLimitIsSpent()
    {
        var constraint = new PausableTimeConstraint(Limit, _time);
        constraint.Reset();

        _time.Advance(Limit);
        constraint.Check();
        _time.Advance(TimeSpan.FromTicks(1));

        var error = Assert.Throws<TimeoutException>(constraint.Check);
        Assert.Equal(new TimeoutException().Message, error.Message);
    }

    [Fact]
    public void ShouldNotCountThePause_WhenTheClockWasPaused()
    {
        var constraint = new PausableTimeConstraint(Limit, _time);
        constraint.Reset();
        _time.Advance(TimeSpan.FromSeconds(20));

        using (constraint.Pause())
            _time.Advance(TimeSpan.FromMinutes(10));
        _time.Advance(TimeSpan.FromSeconds(10));

        constraint.Check();
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Throws<TimeoutException>(constraint.Check);
    }

    [Fact]
    public void ShouldNeverThrow_WhenPaused()
    {
        var constraint = new PausableTimeConstraint(Limit, _time);
        constraint.Reset();
        _time.Advance(TimeSpan.FromMinutes(5));

        using var pause = constraint.Pause();

        Assert.True(constraint.IsPaused);
        constraint.Check();
    }

    [Fact]
    public void ShouldRunTheClockAgain_WhenTheLastNestedPauseEnds()
    {
        var constraint = new PausableTimeConstraint(Limit, _time);
        constraint.Reset();

        var first = constraint.Pause();
        var second = constraint.Pause();
        _time.Advance(TimeSpan.FromMinutes(1));
        first.Dispose();
        first.Dispose();
        Assert.True(constraint.IsPaused);
        _time.Advance(TimeSpan.FromMinutes(1));
        second.Dispose();

        Assert.False(constraint.IsPaused);
        _time.Advance(Limit);
        constraint.Check();
        _time.Advance(TimeSpan.FromTicks(1));
        Assert.Throws<TimeoutException>(constraint.Check);
    }

    [Fact]
    public void ShouldCountThePauseFromTheResetOnly_WhenResetDuringAPause()
    {
        var constraint = new PausableTimeConstraint(Limit, _time);
        constraint.Reset();

        var pause = constraint.Pause();
        _time.Advance(TimeSpan.FromMinutes(1));
        constraint.Reset();
        _time.Advance(TimeSpan.FromMinutes(1));
        pause.Dispose();

        _time.Advance(Limit);
        constraint.Check();
        _time.Advance(TimeSpan.FromTicks(1));
        Assert.Throws<TimeoutException>(constraint.Check);
    }

    [Fact]
    public void ShouldOpenANewWindow_WhenReset()
    {
        var constraint = new PausableTimeConstraint(Limit, _time);
        constraint.Reset();
        _time.Advance(Limit + TimeSpan.FromSeconds(1));

        constraint.Reset();

        constraint.Check();
    }
}
