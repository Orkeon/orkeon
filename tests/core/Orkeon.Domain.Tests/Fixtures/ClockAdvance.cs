using System.Diagnostics;

namespace Orkeon.Domain.Tests.Fixtures;

/// <summary>
/// Deterministic clock-advance helpers (R5.6): replace fixed <c>Thread.Sleep</c> deltas
/// used to "ensure different timestamps" with a bounded wait on the observable condition
/// (the system UTC clock has actually advanced). Synchronous on purpose, so the many
/// non-async domain tests can use it without being rewritten.
/// </summary>
/// <remarks>
/// Once the Domain layer reads time through an injectable <see cref="TimeProvider"/>
/// (chantier horloge, fichier 04), these waits should be replaced by advancing a
/// <c>FakeTimeProvider</c> instead of waiting on the real clock.
/// </remarks>
internal static class ClockAdvance
{
    /// <summary>Generous upper bound to absorb CI jitter; reaching it is a test failure.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Blocks until <see cref="DateTime.UtcNow"/> is strictly greater than
    /// <paramref name="referenceUtc"/> (typically a timestamp captured by the code under
    /// test), so the next timestamp read is guaranteed to differ.
    /// </summary>
    public static void UntilStrictlyAfter(DateTime referenceUtc)
        => Until(() => DateTime.UtcNow > referenceUtc, $"UTC clock did not advance past {referenceUtc:O}");

    /// <summary>
    /// Blocks until the UTC clock has ticked past its value at the time of the call.
    /// Deterministic equivalent of <c>Thread.Sleep(small)</c> between two operations
    /// whose timestamps must differ.
    /// </summary>
    public static void Tick() => UntilStrictlyAfter(DateTime.UtcNow);

    /// <summary>
    /// Blocks until <paramref name="condition"/> becomes <c>true</c>; throws
    /// <see cref="TimeoutException"/> after 10 s. Sync counterpart of
    /// <c>Orkeon.Tests.Shared.Timing.Polling.WaitUntilAsync</c>.
    /// </summary>
    public static void Until(Func<bool> condition, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var start = Stopwatch.GetTimestamp();
        var spinner = new SpinWait();
        while (!condition())
        {
            if (Stopwatch.GetElapsedTime(start) >= Timeout)
            {
                throw new TimeoutException(
                    $"ClockAdvance: {description ?? "condition still false"} after {Timeout.TotalSeconds:0.##} s.");
            }

            spinner.SpinOnce();
        }
    }
}
