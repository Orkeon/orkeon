namespace Orkeon.Host.Gateway;

/// <summary>
/// The answers that send files now: a request of them is in memory until Discord has it, so there are few at once, and one
/// for one person.
/// </summary>
internal sealed class DiscordSends
{
    /// <summary>How many there are at once, in all.</summary>
    internal const int Most = 4;

    private readonly Lock _lock = new();
    private readonly HashSet<ulong> _people = [];

    /// <summary>The place of one answer, until it is disposed; null when there is none.</summary>
    /// <param name="person">Who asked.</param>
    public IDisposable? TryTake(ulong person)
    {
        lock (_lock)
            return _people.Count < Most && _people.Add(person) ? new Place(this, person) : null;
    }

    private sealed class Place(DiscordSends sends, ulong person) : IDisposable
    {
        public void Dispose()
        {
            lock (sends._lock)
                sends._people.Remove(person);
        }
    }
}
