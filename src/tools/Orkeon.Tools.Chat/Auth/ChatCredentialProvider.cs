using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Tools.Abstractions.Auth;
using Orkeon.Tools.Chat.Configuration;

namespace Orkeon.Tools.Chat.Auth;

/// <summary>
/// Produces the access token an account calls its chat with, from the store, refreshed when it
/// is about to expire. It never signs in interactively — that is <c>orkeon chat login</c>'s job —
/// so a tool that finds no usable token says so and stops.
/// </summary>
internal sealed class ChatCredentialProvider
{
    private readonly IChatTokenStore _store;
    private readonly OAuth2Client _oauth;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshGates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ChatTokenSet> _fresh = new(StringComparer.Ordinal);

    /// <summary>Creates the provider.</summary>
    public ChatCredentialProvider(IChatTokenStore store, OAuth2Client oauth)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(oauth);
        _store = store;
        _oauth = oauth;
    }

    /// <summary>
    /// Whether <paramref name="account"/> has what it needs to connect, without any network: a
    /// token (fresh or refreshable) is stored. Null when it
    /// has, else what to do.
    /// </summary>
    public async Task<string?> DiagnoseAsync(ResolvedChatAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        try
        {
            var tokens = await _store.ReadAsync(TokenKey(account), cancellationToken).ConfigureAwait(false);
            return tokens is not null && (_oauth.IsFresh(tokens.ExpiresAt) || tokens.RefreshToken is not null)
                ? null
                : LoginHint(account);
        }
        catch (ChatToolException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// A valid access token for <paramref name="account"/>, refreshed if needed. A token the API
    /// has just refused (<paramref name="refused"/>) is not served again, however fresh it looks:
    /// the store is read again — a new <c>orkeon chat login</c> may have replaced it — and the
    /// refresh token asked for another one.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(ResolvedChatAccount account, CancellationToken cancellationToken, string? refused = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        var key = TokenKey(account);
        if (_fresh.TryGetValue(key, out var cached) && _oauth.IsFresh(cached.ExpiresAt) && cached.AccessToken != refused)
            return cached.AccessToken;

        var gate = Gate(key);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tokens = await _store.ReadAsync(key, cancellationToken).ConfigureAwait(false);
            if (tokens is not null && _oauth.IsFresh(tokens.ExpiresAt) && tokens.AccessToken != refused)
            {
                _fresh[key] = tokens;
                return tokens.AccessToken;
            }

            if (tokens?.RefreshToken is not { } refreshToken)
                throw new ChatToolException(ChatErrorCode.SignInRequired, LoginHint(account));

            ChatTokenSet refreshed;
            try
            {
                refreshed = ToChat(await _oauth.RefreshAsync(account.OAuth, clientSecret: null, refreshToken, cancellationToken).ConfigureAwait(false));
            }
            catch (OAuthException ex) when (ex.Failure == OAuthFailure.LoginRequired && !IsConsent(ex))
            {
                throw new ChatToolException(ChatErrorCode.SignInRequired, $"{LoginHint(account)} ({ex.Message})", ex);
            }
            catch (OAuthException ex)
            {
                throw Translate(ex);
            }

            // A sign-out or a sign-in made during the refresh, by another process too, wins.
            var current = await _store.ReadAsync(key, cancellationToken).ConfigureAwait(false);
            if (current is null)
                throw new ChatToolException(ChatErrorCode.SignInRequired, LoginHint(account));
            if (current.AccessToken != tokens.AccessToken || current.RefreshToken != tokens.RefreshToken)
            {
                if (!_oauth.IsFresh(current.ExpiresAt) || current.AccessToken == refused)
                    throw new ChatToolException(ChatErrorCode.ServerError, $"The tokens of chat account '{account.Name}' changed during their refresh: call again.");
                refreshed = current;
            }
            else
            {
                await _store.WriteAsync(key, refreshed, cancellationToken).ConfigureAwait(false);
            }

            _fresh[key] = refreshed;
            return refreshed.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Stores tokens obtained by an interactive sign-in, in place of every other of the account.</summary>
    public async Task StoreAsync(ResolvedChatAccount account, ChatTokenSet tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(tokens);
        var key = TokenKey(account);
        var gate = Gate(key);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _store.WriteAsync(key, tokens, cancellationToken).ConfigureAwait(false);
            _fresh[key] = tokens;
            await _store.DeleteAccountAsync(account.Name, key, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The tokens stored for <paramref name="account"/>, or null.</summary>
    public Task<ChatTokenSet?> StoredAsync(ResolvedChatAccount account, CancellationToken cancellationToken) =>
        _store.ReadAsync(TokenKey(account), cancellationToken);

    /// <summary>
    /// Forgets every token of the account named <paramref name="name"/>, whatever settings they were
    /// issued under; false when none were stored. <paramref name="account"/> is null when the account
    /// does not resolve: no call of this process uses its tokens then.
    /// </summary>
    public async Task<bool> ForgetAsync(string name, ResolvedChatAccount? account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (account is null)
            return await _store.DeleteAccountAsync(name, keptKey: null, cancellationToken).ConfigureAwait(false);

        var key = TokenKey(account);
        var gate = Gate(key);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _fresh.TryRemove(key, out _);
            return await _store.DeleteAccountAsync(name, keptKey: null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private SemaphoreSlim Gate(string key) => _refreshGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

    /// <summary>
    /// The store key of <paramref name="account"/>'s tokens: its name plus a digest of what the
    /// tokens are valid for (provider, client, API, token endpoint, scopes). Changing any of these —
    /// granting <c>Send</c> to a reading account widens its scopes — asks for a new sign-in
    /// instead of sending a token to a place it was not issued for.
    /// </summary>
    public static string TokenKey(ResolvedChatAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var identity = string.Join('|',
            account.Provider.ToString(),
            account.OAuth.ClientId,
            account.ApiBase.Host,
            account.OAuth.TokenEndpoint.AbsoluteUri,
            string.Join(' ', account.OAuth.Scopes));
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return $"{account.Name}-{digest[..12]}";
    }

    /// <summary>The chat form of tokens the shared OAuth client returned.</summary>
    public static ChatTokenSet ToChat(OAuthTokenSet tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return new ChatTokenSet
        {
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            ExpiresAt = tokens.ExpiresAt,
            Scopes = tokens.Scopes,
        };
    }

    /// <summary>The chat exception of a shared OAuth failure: same message, the matching code.</summary>
    public static ChatToolException Translate(OAuthException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var code = exception.Failure switch
        {
            OAuthFailure.InvalidConfiguration => ChatErrorCode.InvalidConfiguration,
            OAuthFailure.AuthenticationFailed or OAuthFailure.LoginRequired when IsConsent(exception) => ChatErrorCode.ConsentMissing,
            OAuthFailure.AuthenticationFailed or OAuthFailure.LoginRequired => ChatErrorCode.SignInRequired,
            _ => ChatErrorCode.ServerError,
        };
        return new ChatToolException(code, exception.Message, exception);
    }

    // The Microsoft identity platform names a missing consent by these codes.
    private static bool IsConsent(OAuthException exception) =>
        exception.Message.Contains("AADSTS65001", StringComparison.Ordinal)
        || exception.Message.Contains("AADSTS90094", StringComparison.Ordinal);

    /// <summary>The sentence that says how to sign an account in.</summary>
    public static string LoginHint(ResolvedChatAccount account) =>
        $"Chat account '{account.Name}' needs a sign-in: run `orkeon chat login {account.Name}` in a terminal.";
}
