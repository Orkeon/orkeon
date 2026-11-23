using System.Globalization;
using Orkeon.Tools.Chat.Constants;

namespace Orkeon.Tools.Chat.Security;

/// <summary>Spaces the posts in one place: Graph takes one a second per channel and per chat.</summary>
internal sealed class PostPacer
{
    private readonly Dictionary<string, DateTimeOffset> _next = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;

    /// <summary>Creates the pacer over <paramref name="time"/>.</summary>
    public PostPacer(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>Throws when a post in <paramref name="place"/> would wait too long; takes no slot.</summary>
    public void RefuseWhenBusy(string place)
    {
        ArgumentNullException.ThrowIfNull(place);
        lock (_gate)
        {
            if (_next.TryGetValue(place, out var slot) && slot - _time.GetUtcNow() > ChatDefaults.MaxPostWait)
                throw Busy(place);
        }
    }

    /// <summary>
    /// Takes the next slot of <paramref name="place"/> and waits for it. A wait that would be too long
    /// is refused and takes none; a cancelled one gives its slot back when nobody queued behind it.
    /// </summary>
    public async Task WaitAsync(string place, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(place);
        TimeSpan wait;
        DateTimeOffset slot;
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            foreach (var expired in _next.Where(entry => entry.Value <= now).Select(entry => entry.Key).ToList())
                _next.Remove(expired);
            slot = _next.TryGetValue(place, out var next) ? next : now;
            wait = slot - now;
            if (wait > ChatDefaults.MaxPostWait)
                throw Busy(place);
            _next[place] = slot + ChatDefaults.PostInterval;
        }

        if (wait <= TimeSpan.Zero)
            return;

        try
        {
            await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                if (_next.TryGetValue(place, out var next) && next == slot + ChatDefaults.PostInterval)
                    _next[place] = slot;
            }

            throw;
        }
    }

    private static ChatToolException Busy(string place) => new(
        ChatErrorCode.Throttled,
        string.Create(CultureInfo.InvariantCulture,
            $"Too many posts are already waiting for '{place}', one a second: this one would wait more than {ChatDefaults.MaxPostWait.TotalSeconds} s, and nothing was posted. Retry later."));
}
