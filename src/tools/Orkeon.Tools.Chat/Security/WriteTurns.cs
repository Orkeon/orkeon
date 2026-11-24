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

    /// <summary>
    /// Takes a unit of the account's cap and the place's next turn, waits for it, then does
    /// <paramref name="write"/>. A refusal takes neither, and a write the service refused for its
    /// rate, surely not done, gives its unit back.
    /// </summary>
    public Task RunAsync(ChatSession session, ChatTarget target, Func<Task> write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);
        return RunAsync(
            session,
            target,
            async () =>
            {
                await write().ConfigureAwait(false);
                return true;
            },
            cancellationToken);
    }

    /// <summary>The same for a write that answers something.</summary>
    public Task<T> RunAsync<T>(ChatSession session, ChatTarget target, Func<Task<T>> write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(write);
        return RunAsync(session.Account, target.Place, write, cancellationToken);
    }

    /// <summary>The same for a <paramref name="place"/> no target names: a webhook's.</summary>
    public async Task<T> RunAsync<T>(ResolvedChatAccount account, string place, Func<Task<T>> write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(write);
        var slot = _pacer.Reserve(place);
        DateTimeOffset recorded;
        try
        {
            recorded = Consume(account);
        }
        catch (ChatToolException)
        {
            _pacer.GiveBack(slot);
            throw;
        }

        await _pacer.WaitAsync(slot, cancellationToken).ConfigureAwait(false);
        return await AttemptAsync(account, recorded, write).ConfigureAwait(false);
    }

    /// <summary>The same for a chat <paramref name="open"/> finds or opens, which counts in the cap too: the place is not known before.</summary>
    public async Task<T> RunAsync<T>(ChatSession session, Func<Task<ChatTarget>> open, Func<ChatTarget, Task<T>> write, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(write);
        var recorded = Consume(session.Account);
        var (target, slot) = await AttemptAsync(
            session.Account,
            recorded,
            async () =>
            {
                var opened = await open().ConfigureAwait(false);
                return (opened, _pacer.Reserve(opened.Place));
            }).ConfigureAwait(false);

        await _pacer.WaitAsync(slot, cancellationToken).ConfigureAwait(false);
        return await AttemptAsync(session.Account, recorded, () => write(target)).ConfigureAwait(false);
    }

    /// <summary>Throttled is a 429, or the pause one asked for: nothing was done, unlike after any other failure.</summary>
    private async Task<T> AttemptAsync<T>(ResolvedChatAccount account, DateTimeOffset recorded, Func<Task<T>> attempt)
    {
        try
        {
            return await attempt().ConfigureAwait(false);
        }
        catch (ChatToolException ex) when (ex.Code == ChatErrorCode.Throttled)
        {
            _quota.Refund(account.Name, recorded);
            throw;
        }
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
