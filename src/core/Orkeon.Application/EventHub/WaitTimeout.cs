namespace Orkeon.Application.EventHub;

/// <summary>
/// Wait timeout policy: bounded (<see cref="FiniteWaitTimeout"/>) or unbounded (<see cref="ForeverWaitTimeout"/>).
/// </summary>
public abstract record WaitTimeout
{
    private protected WaitTimeout() { }
}

/// <summary>Bounded wait. The crew sleeps → wake-up scheduled at <c>StartedAt + Duration</c>.</summary>
public sealed record FiniteWaitTimeout(TimeSpan Duration) : WaitTimeout
{
    /// <summary>Creates a <see cref="FiniteWaitTimeout"/>, validating that <paramref name="duration"/> is strictly positive.</summary>
    public static FiniteWaitTimeout Of(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration),
                "WaitTimeout.Finite requires a strictly positive duration.");
        return new FiniteWaitTimeout(duration);
    }
}

/// <summary>Unbounded wait. Woken only by the arrival of a matching message.</summary>
public sealed record ForeverWaitTimeout : WaitTimeout
{
    /// <summary>Singleton instance.</summary>
    public static readonly ForeverWaitTimeout Instance = new();
}
