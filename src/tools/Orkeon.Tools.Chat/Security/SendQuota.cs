using System.Collections.Concurrent;

namespace Orkeon.Tools.Chat.Security;

/// <summary>
/// The per-account hourly cap on posts (<c>Send:MaxPerHour</c>), counted in this process over a
/// sliding hour. An attempt counts, whatever became of it.
/// </summary>
internal sealed class SendQuota
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _sent = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeProvider _time;

    /// <summary>Creates the quota over <paramref name="time"/>.</summary>
    public SendQuota(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>
    /// Records one post for <paramref name="account"/> when it fits under <paramref name="maxPerHour"/>;
    /// returns false, recording nothing, when the hour is full. A null cap always fits.
    /// </summary>
    public bool TryConsume(string account, int? maxPerHour)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (maxPerHour is not { } cap)
            return true;

        var now = _time.GetUtcNow();
        var sent = _sent.GetOrAdd(account, _ => new Queue<DateTimeOffset>());
        lock (sent)
        {
            while (sent.Count > 0 && now - sent.Peek() >= Window)
                sent.Dequeue();

            if (sent.Count >= cap)
                return false;

            sent.Enqueue(now);
            return true;
        }
    }
}
