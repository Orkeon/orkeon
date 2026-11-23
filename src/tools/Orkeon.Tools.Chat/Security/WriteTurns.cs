using System.Globalization;
using Orkeon.Tools.Chat.Configuration;
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

    /// <summary>Takes a unit of the account's cap and the place's next turn, then waits for it. A refusal takes neither.</summary>
    public Task TakeAsync(ChatSession session, ChatTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(target);
        return TakeAsync(session.Account, target.Place, cancellationToken);
    }

    /// <summary>The same for a <paramref name="place"/> no target names: a webhook's.</summary>
    public async Task TakeAsync(ResolvedChatAccount account, string place, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(place);
        var slot = _pacer.Reserve(place);
        try
        {
            Consume(account);
        }
        catch (ChatToolException)
        {
            _pacer.GiveBack(slot);
            throw;
        }

        await _pacer.WaitAsync(slot, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The same for a chat <paramref name="open"/> finds or opens, which counts in the cap too: the
    /// place is not known before. A refusal for the rate, of either, takes no unit.
    /// </summary>
    public async Task<ChatTarget> TakeAsync(ChatSession session, Func<Task<ChatTarget>> open, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(open);
        var recorded = Consume(session.Account);
        ChatTarget target;
        PostSlot slot;
        try
        {
            target = await open().ConfigureAwait(false);
            slot = _pacer.Reserve(target.Place);
        }
        catch (ChatToolException ex) when (ex.Code == ChatErrorCode.Throttled)
        {
            _quota.Refund(session.Account.Name, recorded);
            throw;
        }

        await _pacer.WaitAsync(slot, cancellationToken).ConfigureAwait(false);
        return target;
    }

    private DateTimeOffset Consume(ResolvedChatAccount account)
    {
        if (_quota.TryConsume(account.Name, account.MaxPerHour, out var recorded))
            return recorded;
        throw new ChatToolException(
            ChatErrorCode.Throttled,
            string.Create(CultureInfo.InvariantCulture,
                $"Chat account '{account.Name}' already attempted {account.MaxPerHour} messages in the last hour (Send:MaxPerHour); nothing was posted. Edits, deletions and reactions count in the cap too, and nothing was changed."));
    }
}
