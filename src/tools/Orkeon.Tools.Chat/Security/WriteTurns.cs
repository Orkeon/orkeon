using System.Globalization;
using Orkeon.Tools.Chat.Providers;

namespace Orkeon.Tools.Chat.Security;

/// <summary>
/// The bounds every write of the family goes through: the account's hourly cap and the pace of a
/// place. An edit, a deletion and a reaction count as a post does, since each shows in the place
/// or notifies someone, and a cap that only counted posts would leave the rest without one.
/// </summary>
internal sealed class WriteTurns
{
    private readonly SendQuota _quota;
    private readonly PostPacer _pacer;

    /// <summary>Creates the bounds over the process's <paramref name="quota"/> and <paramref name="pacer"/>.</summary>
    public WriteTurns(SendQuota quota, PostPacer pacer)
    {
        ArgumentNullException.ThrowIfNull(quota);
        ArgumentNullException.ThrowIfNull(pacer);
        _quota = quota;
        _pacer = pacer;
    }

    /// <summary>Takes one unit of the account's cap; refuses, taking none, when the place is too busy.</summary>
    public void Take(ChatSession session, ChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(target);
        _pacer.RefuseWhenBusy(target.Place);
        if (_quota.TryConsume(session.Account.Name, session.Account.MaxPerHour))
            return;
        throw new ChatToolException(
            ChatErrorCode.Throttled,
            string.Create(CultureInfo.InvariantCulture,
                $"Chat account '{session.Account.Name}' already attempted {session.Account.MaxPerHour} messages in the last hour (Send:MaxPerHour); nothing was posted. Edits, deletions and reactions count in the cap too, and nothing was changed."));
    }

    /// <summary>Waits for the place's turn.</summary>
    public Task WaitAsync(ChatTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        return _pacer.WaitAsync(target.Place, cancellationToken);
    }
}
