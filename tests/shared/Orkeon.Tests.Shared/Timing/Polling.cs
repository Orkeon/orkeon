using System.Diagnostics;

namespace Orkeon.Tests.Shared.Timing;

/// <summary>
/// Deterministic condition-polling helpers for tests (R5.6): replaces fixed
/// <c>Task.Delay</c> sleeps with a bounded wait on an observable condition.
/// </summary>
public static class Polling
{
    /// <summary>Default maximum wait time (generous, to absorb CI jitter).</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Default delay between two condition checks.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Polls <paramref name="condition"/> until it returns <c>true</c>.
    /// Throws <see cref="TimeoutException"/> if it is still <c>false</c> after
    /// <paramref name="timeout"/> (default 10 s, checked every 25 ms).
    /// </summary>
    public static Task WaitUntilAsync(
        Func<bool> condition,
        TimeSpan? timeout = null,
        TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return WaitUntilAsync(() => Task.FromResult(condition()), timeout, interval);
    }

    /// <summary>Async-condition variant of <see cref="WaitUntilAsync(Func{bool}, TimeSpan?, TimeSpan?)"/>.</summary>
    public static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan? timeout = null,
        TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(condition);

        var effectiveTimeout = timeout ?? DefaultTimeout;
        var effectiveInterval = interval ?? DefaultInterval;
        var start = Stopwatch.GetTimestamp();

        while (true)
        {
            if (await condition().ConfigureAwait(false))
            {
                return;
            }

            if (Stopwatch.GetElapsedTime(start) >= effectiveTimeout)
            {
                throw new TimeoutException(
                    $"Polling.WaitUntilAsync: condition still false after " +
                    $"{effectiveTimeout.TotalSeconds:0.##} s (polled every " +
                    $"{effectiveInterval.TotalMilliseconds:0} ms).");
            }

            await Task.Delay(effectiveInterval).ConfigureAwait(false);
        }
    }
}
