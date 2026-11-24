using System.Collections.Concurrent;

namespace Orkeon.Tools.Chat.Security;

/// <summary>
/// The per-account hourly cap on writes (<c>Send:MaxPerHour</c>), counted in this process over a
/// sliding hour. An attempt counts, whatever became of it.
/// </summary>
internal sealed class SendQuota
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, List<DateTimeOffset>> _sent = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;

    /// <summary>Creates the quota over <paramref name="time"/>.</summary>
    public SendQuota(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>Takes the write of <paramref name="account"/> recorded at <paramref name="at"/> out of the count: it was refused before any attempt.</summary>
    public void Refund(string account, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (!_sent.TryGetValue(account, out var sent))
            return;
        lock (sent)
        {
            sent.Remove(at);
        }
    }

    /// <summary>
    /// Records one write for <paramref name="account"/> when it fits under <paramref name="maxPerHour"/>;
    /// returns false, recording nothing, when the hour is full. A null cap always fits.
    /// </summary>
    public bool TryConsume(string account, int? maxPerHour) => TryConsume(account, maxPerHour, out _);

    /// <summary>The same, with the time recorded, which a refund names.</summary>
    public bool TryConsume(string account, int? maxPerHour, out DateTimeOffset now)
    {
        now = _time.GetUtcNow();
        ArgumentNullException.ThrowIfNull(account);
        if (maxPerHour is not { } cap)
            return true;

        var recorded = now;
        var sent = _sent.GetOrAdd(account, _ => []);
        lock (sent)
        {
            sent.RemoveAll(at => recorded - at >= Window);
            if (sent.Count >= cap)
                return false;

            sent.Add(now);
            return true;
        }
    }
}
