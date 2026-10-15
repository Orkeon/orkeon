namespace Orkeon.Scripting.Tests.Doubles;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when the test calls <see cref="Advance"/>:
/// a wall-clock limit is then exercised without waiting for it.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    /// <inheritdoc />
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    /// <inheritdoc />
    public override long GetTimestamp() => GetUtcNow().UtcTicks;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by)
    {
        lock (_gate)
            _now += by;
    }
}
