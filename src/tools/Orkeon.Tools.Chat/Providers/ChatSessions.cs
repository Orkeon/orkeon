using System.Collections.Concurrent;
using Orkeon.Tools.Chat.Auth;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Places;
using Orkeon.Tools.Chat.Providers.Teams;

namespace Orkeon.Tools.Chat.Providers;

/// <summary>An account in use: its provider and its resolved places.</summary>
internal sealed record ChatSession(ResolvedChatAccount Account, IChatProvider Provider, PlaceGate Gate);

/// <summary>Opens each account once and keeps it: the allowed places are resolved at its first use.</summary>
internal sealed class ChatSessions : IDisposable
{
    private readonly Func<ResolvedChatAccount, IChatProvider> _open;
    private readonly ConcurrentDictionary<string, Lazy<ChatSession>> _sessions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the sessions over the providers <paramref name="open"/> returns.</summary>
    public ChatSessions(Func<ResolvedChatAccount, IChatProvider> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        _open = open;
    }

    /// <summary>Sessions over the provider each account declares.</summary>
    public static ChatSessions Over(ChatCredentialProvider credentials, Func<HttpClient> http, TimeProvider time) =>
        new(account => new TeamsProvider(new GraphClient(http, account, credentials, time)));

    /// <summary>The session of <paramref name="account"/>.</summary>
    public ChatSession Get(ResolvedChatAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return _sessions.GetOrAdd(account.Name, _ => new Lazy<ChatSession>(() =>
        {
            var provider = _open(account);
            return new ChatSession(account, provider, new PlaceGate(account, provider));
        })).Value;
    }

    /// <summary>Forgets the session of an account that signed in or out: another user may be there now.</summary>
    public void Forget(ResolvedChatAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        _sessions.TryRemove(account.Name, out _);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var session in _sessions.Values.Where(session => session.IsValueCreated))
            session.Value.Gate.Dispose();
    }
}
