using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Orkeon.Tools.Email.Configuration;

namespace Orkeon.Tools.Email.Auth;

/// <summary>What a protocol client authenticates with.</summary>
/// <param name="Username">The login name.</param>
internal abstract record EmailCredential(string Username);

/// <summary>A password read from the environment.</summary>
/// <param name="Username">The login name.</param>
/// <param name="Password">The password (an app password for Gmail).</param>
internal sealed record PasswordCredential(string Username, string Password) : EmailCredential(Username)
{
    /// <summary>Never prints the password.</summary>
    public override string ToString() => $"PasswordCredential {{ Username = {Username} }}";
}

/// <summary>An OAuth access token, sent as XOAUTH2 or as a bearer header.</summary>
/// <param name="Username">The login name.</param>
/// <param name="AccessToken">The token.</param>
internal sealed record BearerCredential(string Username, string AccessToken) : EmailCredential(Username)
{
    /// <summary>Never prints the token.</summary>
    public override string ToString() => $"BearerCredential {{ Username = {Username} }}";
}

/// <summary>Reads environment variables; replaced in tests so they never touch the process environment.</summary>
/// <param name="Read">Returns a variable's value, or null.</param>
internal sealed record EmailEnvironment(Func<string, string?> Read)
{
    /// <summary>The process environment.</summary>
    public static EmailEnvironment Process { get; } = new(Environment.GetEnvironmentVariable);
}

/// <summary>
/// Produces the credential an account connects with: its password from the named environment
/// variable, or an OAuth access token from the store, refreshed when it is about to expire.
/// It never signs in interactively — that is <c>orkeon email login</c>'s job — so a tool that
/// finds no usable token says so and stops.
/// </summary>
internal sealed class EmailCredentialProvider
{
    private readonly IEmailTokenStore _store;
    private readonly OAuth2Client _oauth;
    private readonly EmailEnvironment _environment;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshGates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, EmailTokenSet> _fresh = new(StringComparer.Ordinal);

    /// <summary>Creates the provider.</summary>
    public EmailCredentialProvider(IEmailTokenStore store, OAuth2Client oauth, EmailEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(oauth);
        ArgumentNullException.ThrowIfNull(environment);
        _store = store;
        _oauth = oauth;
        _environment = environment;
    }

    /// <summary>The store tokens are kept in.</summary>
    public IEmailTokenStore Store => _store;

    /// <summary>The credential <paramref name="account"/> connects with.</summary>
    public async Task<EmailCredential> GetAsync(ResolvedEmailAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.Auth.Method == EmailAuthMethod.Password)
            return new PasswordCredential(account.Auth.Username, ReadPassword(account));

        var token = await GetAccessTokenAsync(account, cancellationToken).ConfigureAwait(false);
        return new BearerCredential(account.Auth.Username, token);
    }

    /// <summary>
    /// Whether <paramref name="account"/> has what it needs to connect, without any network:
    /// the password variable is set, or the client secret it names is set and a token (fresh
    /// or refreshable) is stored.
    /// </summary>
    public async Task<string?> DiagnoseAsync(ResolvedEmailAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        try
        {
            if (account.Auth.Method == EmailAuthMethod.Password)
            {
                _ = ReadPassword(account);
                return null;
            }

            // A refresh needs the client secret too, when the account names one (Google).
            _ = ReadClientSecret(account);
            var tokens = await _store.ReadAsync(TokenKey(account), cancellationToken).ConfigureAwait(false);
            return tokens is not null && (_oauth.IsFresh(tokens) || tokens.RefreshToken is not null)
                ? null
                : LoginHint(account);
        }
        catch (EmailToolException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>The OAuth client secret of <paramref name="account"/>, or null when it declares none.</summary>
    public string? ReadClientSecret(ResolvedEmailAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var name = account.Auth.OAuth?.ClientSecretEnvVar;
        if (name is null)
            return null;

        var value = _environment.Read(name);
        if (string.IsNullOrEmpty(value))
        {
            throw new EmailToolException(
                EmailErrorCode.CredentialMissing,
                $"The OAuth client secret of e-mail account '{account.Name}' is read from the environment variable {name}, which is not set.");
        }

        return value;
    }

    /// <summary>A valid access token for <paramref name="account"/>, refreshed if needed.</summary>
    public async Task<string> GetAccessTokenAsync(ResolvedEmailAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var settings = account.Auth.OAuth
            ?? throw new EmailToolException(EmailErrorCode.InvalidConfiguration, $"E-mail account '{account.Name}' does not use OAuth2.");
        var key = TokenKey(account);
        if (_fresh.TryGetValue(key, out var cached) && _oauth.IsFresh(cached))
            return cached.AccessToken;

        var gate = _refreshGates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tokens = await _store.ReadAsync(key, cancellationToken).ConfigureAwait(false);
            if (tokens is not null && _oauth.IsFresh(tokens))
            {
                _fresh[key] = tokens;
                return tokens.AccessToken;
            }

            if (tokens?.RefreshToken is not { } refreshToken)
                throw new EmailToolException(EmailErrorCode.LoginRequired, LoginHint(account));

            EmailTokenSet refreshed;
            try
            {
                refreshed = await _oauth.RefreshAsync(settings, ReadClientSecret(account), refreshToken, cancellationToken).ConfigureAwait(false);
            }
            catch (EmailToolException ex) when (ex.Code == EmailErrorCode.LoginRequired)
            {
                throw new EmailToolException(EmailErrorCode.LoginRequired, $"{LoginHint(account)} ({ex.Message})", ex);
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
    public async Task StoreAsync(ResolvedEmailAccount account, EmailTokenSet tokens, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(tokens);
        var key = TokenKey(account);
        await _store.WriteAsync(key, tokens, cancellationToken).ConfigureAwait(false);
        _fresh[key] = tokens;
    }

    /// <summary>Forgets <paramref name="account"/>'s tokens; false when none were stored.</summary>
    public Task<bool> ForgetAsync(ResolvedEmailAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var key = TokenKey(account);
        _fresh.TryRemove(key, out _);
        return _store.DeleteAsync(key, cancellationToken);
    }

    /// <summary>
    /// The store key of <paramref name="account"/>'s tokens: its name plus a digest of what the
    /// tokens are valid for (address, client, token endpoint, scopes). Changing any of these
    /// asks for a new sign-in instead of sending a token to a place it was not issued for.
    /// </summary>
    public static string TokenKey(ResolvedEmailAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        var oauth = account.Auth.OAuth;
        var identity = string.Join('|',
            account.Address,
            oauth?.ClientId ?? string.Empty,
            oauth?.TokenEndpoint.AbsoluteUri ?? string.Empty,
            string.Join(' ', oauth?.Scopes ?? []));
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return $"{account.Name}-{digest[..12]}";
    }

    private string ReadPassword(ResolvedEmailAccount account)
    {
        var name = account.Auth.PasswordEnvVar
            ?? throw new EmailToolException(EmailErrorCode.InvalidConfiguration, $"E-mail account '{account.Name}' names no password variable (Auth:PasswordEnvVar).");
        var value = _environment.Read(name);
        if (string.IsNullOrEmpty(value))
        {
            throw new EmailToolException(
                EmailErrorCode.CredentialMissing,
                $"The password of e-mail account '{account.Name}' is read from the environment variable {name}, which is not set.");
        }

        return value;
    }

    private static string LoginHint(ResolvedEmailAccount account) =>
        $"E-mail account '{account.Name}' needs an OAuth sign-in: run `orkeon email login {account.Name}` in a terminal.";
}
