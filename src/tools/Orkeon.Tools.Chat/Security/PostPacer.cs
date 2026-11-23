using System.Globalization;
using Orkeon.Tools.Chat.Constants;

namespace Orkeon.Tools.Chat.Security;

/// <summary>The turn of one post: the place it is in and when it comes.</summary>
internal sealed record PostSlot(string Place, DateTimeOffset At);

/// <summary>Spaces the posts in one place: Graph takes one a second per channel and per chat.</summary>
internal sealed class PostPacer
{
    private readonly Dictionary<string, DateTimeOffset> _next = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;

    /// <summary>Creates the pacer over <paramref name="time"/>.</summary>
    public PostPacer(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    /// <summary>
    /// Takes the next slot of <paramref name="place"/>. A wait that would be too long is refused and
    /// takes none. The slot is waited for with <see cref="WaitAsync(PostSlot, CancellationToken)"/> or given back.
    /// </summary>
    public PostSlot Reserve(string place)
    {
        ArgumentNullException.ThrowIfNull(place);
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            foreach (var expired in _next.Where(entry => entry.Value <= now).Select(entry => entry.Key).ToList())
                _next.Remove(expired);
            var at = _next.TryGetValue(place, out var next) ? next : now;
            if (at - now > ChatDefaults.MaxPostWait)
                throw Busy(place);
            _next[place] = at + ChatDefaults.PostInterval;
            return new PostSlot(place, at);
        }
    }

    /// <summary>Gives <paramref name="slot"/> back when nobody queued behind it.</summary>
    public void GiveBack(PostSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        lock (_gate)
        {
            if (_next.TryGetValue(slot.Place, out var next) && next == slot.At + ChatDefaults.PostInterval)
                _next[slot.Place] = slot.At;
        }
    }

    /// <summary>Waits for <paramref name="slot"/>; a cancelled wait gives it back.</summary>
    public async Task WaitAsync(PostSlot slot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slot);
        var wait = slot.At - _time.GetUtcNow();
        if (wait <= TimeSpan.Zero)
            return;

        try
        {
            await Task.Delay(wait, _time, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            GiveBack(slot);
            throw;
        }
    }

    /// <summary>Takes the next slot of <paramref name="place"/> and waits for it.</summary>
    public async Task WaitAsync(string place, CancellationToken cancellationToken) =>
        await WaitAsync(Reserve(place), cancellationToken).ConfigureAwait(false);

    private static ChatToolException Busy(string place) => new(
        ChatErrorCode.Throttled,
        string.Create(CultureInfo.InvariantCulture,
            $"Too many posts are already waiting for '{place}', one a second: this one would wait more than {ChatDefaults.MaxPostWait.TotalSeconds} s, and nothing was posted. Retry later."));
}
