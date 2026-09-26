namespace Orkeon.Tools.Email.Tests.Doubles;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock only moves when a test says so, so token expiry,
/// the hourly send quota and the folder cache can be asserted to the second.
/// </summary>
public sealed class FakeTimeProvider : TimeProvider
{
    /// <summary>The instant a new provider starts at.</summary>
    public static readonly DateTimeOffset DefaultStart = new(2026, 9, 26, 9, 0, 0, TimeSpan.Zero);

    private readonly Lock _gate = new();
    private DateTimeOffset _now;

    /// <summary>Creates the provider at <paramref name="start"/>, or at <see cref="DefaultStart"/>.</summary>
    public FakeTimeProvider(DateTimeOffset? start = null) => _now = start ?? DefaultStart;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _now;
    }

    /// <summary>Moves the clock forward by <paramref name="duration"/>.</summary>
    public void Advance(TimeSpan duration)
    {
        lock (_gate)
            _now += duration;
    }
}
