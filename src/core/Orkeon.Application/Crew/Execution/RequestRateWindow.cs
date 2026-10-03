namespace Orkeon.Application.Crew.Execution;

/// <summary>
/// The instants of the last model requests of one agent or one crew, over a sliding minute
/// (GAP-38) — the shape of the e-mail send quota, with a wait instead of a refusal. Counted on the
/// monotonic clock of the <see cref="TimeProvider"/> that created it, never the wall clock. It keeps
/// no limit — the caller reads its own at each request — and holds no timer: held by a
/// <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey, TValue}"/> on its agent or
/// crew, it lives with that object and has nothing to release.
/// </summary>
internal sealed class RequestRateWindow
{
    /// <summary>The window's length: requests per minute.</summary>
    internal static readonly TimeSpan Length = TimeSpan.FromMinutes(1);

    private readonly Queue<long> _stamps = new();

    internal RequestRateWindow(TimeProvider time) => Time = time;

    /// <summary>The clock the window counts on, the one that created it.</summary>
    internal TimeProvider Time { get; }

    /// <summary>The lock a request reads and writes the window under.</summary>
    internal Lock Gate { get; } = new();

    /// <summary>
    /// How long a request must wait before the window has room under <paramref name="limit"/>;
    /// zero when it has room now. Called with <see cref="Gate"/> held.
    /// </summary>
    internal TimeSpan WaitUnder(int limit)
    {
        var now = Time.GetTimestamp();
        while (_stamps.Count > 0 && Time.GetElapsedTime(_stamps.Peek(), now) >= Length)
            _stamps.Dequeue();

        if (_stamps.Count < limit)
            return TimeSpan.Zero;

        // The request that must leave for one more to fit: the oldest, unless a lowered limit leaves
        // more than one too many.
        var leaving = _stamps.ElementAt(_stamps.Count - limit);
        return Length - Time.GetElapsedTime(leaving, now);
    }

    /// <summary>Counts one request, now. Called with <see cref="Gate"/> held, after <see cref="WaitUnder"/> said zero.</summary>
    internal void Record() => _stamps.Enqueue(Time.GetTimestamp());
}
