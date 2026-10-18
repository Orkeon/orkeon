using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Tools.Abstractions.Auth;
using Orkeon.Tools.Drive.Configuration;

namespace Orkeon.Tools.Drive.Auth;

/// <summary>
/// Produces the access token an account calls its drive with, from the store, refreshed when it
/// is about to expire. It never signs in interactively — that is <c>orkeon drive login</c>'s job —
/// so a tool that finds no usable token says so and stops.
/// </summary>
internal sealed class DriveCredentialProvider
{
    private readonly IDriveTokenStore _store;
    private readonly OAuth2Client _oauth;
    private readonly SecretEnvironment _environment;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshGates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DriveTokenSet> _fresh = new(StringComparer.Ordinal);

    /// <summary>Creates the provider.</summary>
    public DriveCredentialProvider(IDriveTokenStore store, OAuth2Client oauth, SecretEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(oauth);
        ArgumentNullException.ThrowIfNull(environment);
        _store = store;
        _oauth = oauth;
        _environment = environment;
    }

    /// <summary>
    /// Whether <paramref name="account"/> has what it needs to connect, without any network: the
    /// client secret it names is set, and a token (fresh or refreshable) is stored. Null when it
    /// has, else what to do.
    /// </summary>
    public async Task<string?> DiagnoseAsync(ResolvedDriveAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        try
        {
            // A refresh needs the client secret too, when the account names one (Google).
            _ = ReadClientSecret(account);
            var tokens = await _store.ReadAsync(TokenKey(account), cancellationToken).ConfigureAwait(false);
            return tokens is not null && (_oauth.IsFresh(tokens.ExpiresAt) || tokens.RefreshToken is not null)
                ? null
                : LoginHint(account);
        }
        catch (DriveToolException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>The OAuth client secret of <paramref name="account"/>, or null when it declares none.</summary>
    public string? ReadClientSecret(ResolvedDriveAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var name = account.OAuth.ClientSecretEnvVar;
        if (name is null)
            return null;

        var value = _environment.Read(name);
        if (string.IsNullOrEmpty(value))
        {
            throw new DriveToolException(
                DriveErrorCode.CredentialMissing,
                $"The OAuth client secret of drive account '{account.Name}' is read from the environment variable {name}, which {_environment.Unset}.");
        }

        return value;
    }

    /// <summary>
    /// A valid access token for <paramref name="account"/>, refreshed if needed. A token the API
    /// has just refused (<paramref name="refused"/>) is not served again, however fresh it looks:
    /// the store is read again — a new <c>orkeon drive login</c> may have replaced it — and the
    /// refresh token asked for another one.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(ResolvedDriveAccount account, CancellationToken cancellationToken, string? refused = null)
    {
        ArgumentNullException.ThrowIfNull(account);
        var key = TokenKey(account);
        if (_fresh.TryGetValue(key, out var cached) && _oauth.IsFresh(cached.ExpiresAt) && cached.AccessToken != refused)
            return cached.AccessToken;

        var gate = _refreshGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
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
                throw new DriveToolException(DriveErrorCode.LoginRequired, LoginHint(account));

            DriveTokenSet refreshed;
            try
            {
                refreshed = ToDrive(await _oauth.RefreshAsync(account.OAuth, ReadClientSecret(account), refreshToken, cancellationToken).ConfigureAwait(false));
            }
            catch (OAuthException ex) when (ex.Failure == OAuthFailure.LoginRequired)
            {
                throw new DriveToolException(DriveErrorCode.LoginRequired, $"{LoginHint(account)} ({ex.Message})", ex);
            }
            catch (OAuthException ex)
            {
                throw Translate(ex);
            }

            await _store.WriteAsync(key, refreshed, cancellationToken).ConfigureAwait(false);
            _fresh[key] = refreshed;
            return refreshed.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Stores tokens obtained by an interactive sign-in.</summary>
    public async Task StoreAsync(ResolvedDriveAccount account, DriveTokenSet tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(tokens);
        var key = TokenKey(account);
        await _store.WriteAsync(key, tokens, cancellationToken).ConfigureAwait(false);
        _fresh[key] = tokens;
    }

    /// <summary>Forgets <paramref name="account"/>'s tokens; false when none were stored.</summary>
    public Task<bool> ForgetAsync(ResolvedDriveAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var key = TokenKey(account);
        _fresh.TryRemove(key, out _);
        return _store.DeleteAsync(key, cancellationToken);
    }

    /// <summary>
    /// The store key of <paramref name="account"/>'s tokens: its name plus a digest of what the
    /// tokens are valid for (provider, client, token endpoint, scopes). Changing any of these —
    /// granting <c>Write</c> to a read-only account widens its scopes — asks for a new sign-in
    /// instead of sending a token to a place it was not issued for.
    /// </summary>
    public static string TokenKey(ResolvedDriveAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var identity = string.Join('|',
            account.Provider.ToString(),
            account.OAuth.ClientId,
            account.OAuth.TokenEndpoint.AbsoluteUri,
            string.Join(' ', account.OAuth.Scopes));
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return $"{account.Name}-{digest[..12]}";
    }

    /// <summary>The drive form of tokens the shared OAuth client returned.</summary>
    public static DriveTokenSet ToDrive(OAuthTokenSet tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        return new DriveTokenSet
        {
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            ExpiresAt = tokens.ExpiresAt,
            Scopes = tokens.Scopes,
        };
    }

    /// <summary>The drive exception of a shared OAuth failure: same message, the matching code.</summary>
    public static DriveToolException Translate(OAuthException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var code = exception.Failure switch
        {
            OAuthFailure.InvalidConfiguration => DriveErrorCode.InvalidConfiguration,
            OAuthFailure.AuthenticationFailed => DriveErrorCode.AuthenticationFailed,
            OAuthFailure.LoginRequired => DriveErrorCode.LoginRequired,
            _ => DriveErrorCode.ServerError,
        };
        return new DriveToolException(code, exception.Message, exception);
    }

    private static string LoginHint(ResolvedDriveAccount account) =>
        $"Drive account '{account.Name}' needs an OAuth sign-in: run `orkeon drive login {account.Name}` in a terminal.";
}
