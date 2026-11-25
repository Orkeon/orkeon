using System.Security.Cryptography;

namespace Orkeon.Host.Gateway;

/// <summary>A file offered by a consent card, held until the person it was offered to answers.</summary>
internal sealed class PendingConsent
{
    public required string SenderId { get; init; }
    public required TeamsReplyTarget ReplyTo { get; init; }
    public required OutboundFile File { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// The files waiting for an answer, in memory: bounded, each one for an hour, and taken once, by the person and in the
/// conversation it was offered to. A restart of the host loses them.
/// </summary>
internal sealed class TeamsConsents(TimeProvider time)
{
    internal const int PerConversation = 10;
    internal const int Total = 256;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly Lock _lock = new();
    private readonly Dictionary<string, PendingConsent> _pending = new(StringComparer.Ordinal);

    /// <summary>Holds a file under an identifier nobody can guess; null when too many wait.</summary>
    public string? TryAdd(string senderId, TeamsReplyTarget replyTo, OutboundFile file)
    {
        ArgumentNullException.ThrowIfNull(replyTo);
        lock (_lock)
        {
            var now = time.GetUtcNow();
            foreach (var expired in _pending.Where(p => p.Value.ExpiresAt <= now).Select(p => p.Key).ToList())
                _pending.Remove(expired);
            if (_pending.Count >= Total || _pending.Values.Count(p => p.ReplyTo.ConversationId == replyTo.ConversationId) >= PerConversation)
                return null;

            var id = RandomNumberGenerator.GetHexString(32, lowercase: true);
            _pending.Add(id, new PendingConsent { SenderId = senderId, ReplyTo = replyTo, File = file, ExpiresAt = now + Lifetime });
            return id;
        }
    }

    /// <summary>Takes a file, once; null when none waits for this person in this conversation.</summary>
    public PendingConsent? Take(string? id, string senderId, string conversationId)
    {
        lock (_lock)
        {
            if (id is null
                || !_pending.TryGetValue(id, out var pending)
                || !string.Equals(pending.SenderId, senderId, StringComparison.OrdinalIgnoreCase)
                || pending.ReplyTo.ConversationId != conversationId)
            {
                return null;
            }

            _pending.Remove(id);
            return pending.ExpiresAt > time.GetUtcNow() ? pending : null;
        }
    }

    /// <summary>Lets go of a file whose card was not shown.</summary>
    public void Drop(string id)
    {
        lock (_lock)
            _pending.Remove(id);
    }
}
