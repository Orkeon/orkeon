using System.Text.Json.Serialization;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes;

namespace Orkeon.Tools.Email.Administration;

/// <summary>How an interactive sign-in talks to the person at the terminal.</summary>
public interface IEmailLoginInteraction
{
    /// <summary>
    /// Tells the user to open <paramref name="verificationUri"/> and type <paramref name="userCode"/>
    /// (device sign-in). The code lives <paramref name="expiresIn"/>: past that, the sign-in fails
    /// and has to be started again.
    /// </summary>
    Task ShowDeviceCodeAsync(string account, Uri verificationUri, string userCode, TimeSpan expiresIn, CancellationToken cancellationToken);

    /// <summary>Tells the user to open <paramref name="authorizationUri"/> in a browser (loopback sign-in).</summary>
    Task ShowAuthorizationUrlAsync(string account, Uri authorizationUri, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a redirect address the user pasted — the fallback when the browser cannot reach this
    /// machine's loopback port (WSL, a container, a remote shell). Returns null when nothing can be read.
    /// It may block its thread until a line comes, as a console read does: the sign-in calls it
    /// apart from the wait for the browser.
    /// </summary>
    Task<string?> ReadRedirectAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Tells the user that the line just pasted is not the address the browser ended on, and
    /// <paramref name="reason"/> why: one short sentence, which never repeats what was pasted (an
    /// authorization code is a secret while it lives). The sign-in is not over: it goes on waiting
    /// for the browser, or for another line.
    /// </summary>
    Task ShowRedirectRejectedAsync(string account, string reason, CancellationToken cancellationToken);
}

/// <summary>
/// One declared account, as <c>orkeon email accounts</c> lists it — with <c>--json</c>, under the
/// names <c>email_accounts</c> uses. No secret ever appears here.
/// </summary>
public sealed record EmailAccountStatus
{
    /// <summary>The account name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Its address, when the declaration has a valid one.</summary>
    [JsonPropertyName("address")]
    public string? Address { get; init; }

    /// <summary>The preset.</summary>
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    /// <summary>How it reads mail (<c>Imap</c>, <c>Pop3</c>, <c>Graph</c>).</summary>
    [JsonPropertyName("reads")]
    public string? Reads { get; init; }

    /// <summary>How it sends mail (<c>Smtp</c>, <c>Graph</c>), or null.</summary>
    [JsonPropertyName("sends")]
    public string? Sends { get; init; }

    /// <summary>The rights it grants.</summary>
    [JsonPropertyName("rights")]
    public string? Rights { get; init; }

    /// <summary>How it signs in (<c>Password</c>, <c>OAuth2</c>).</summary>
    [JsonPropertyName("auth")]
    public string? Auth { get; init; }

    /// <summary>Whether it is the account a call without <c>account</c> uses.</summary>
    [JsonPropertyName("default")]
    public bool IsDefault { get; init; }

    /// <summary>Whether it has everything it needs to connect (checked without network).</summary>
    [JsonPropertyName("ready")]
    public bool Ready { get; init; }

    /// <summary>What to fix, when it is not ready.</summary>
    [JsonPropertyName("problem")]
    public string? Problem { get; init; }
}

/// <summary>What <c>orkeon email check</c> found by actually connecting.</summary>
public sealed record EmailCheckResult
{
    /// <summary>The account name.</summary>
    public required string Account { get; init; }

    /// <summary>Folders listed.</summary>
    public required int Folders { get; init; }

    /// <summary>Messages in the inbox, when the server says.</summary>
    public int? InboxTotal { get; init; }

    /// <summary>Unread messages in the inbox, when the server says.</summary>
    public int? InboxUnread { get; init; }
}

/// <summary>
/// The operator's side of the e-mail family: list the accounts, sign an OAuth account in or
/// out, and check that an account connects. Used by <c>orkeon email</c>; never by an agent.
/// </summary>
public sealed class EmailAccountAdministration
{
    private readonly IEmailAccountRegistry _accounts;
    private readonly EmailCredentialProvider _credentials;
    private readonly OAuth2Client _oauth;
    private readonly IMailboxProvider _mailboxes;

    internal EmailAccountAdministration(
        IEmailAccountRegistry accounts, EmailCredentialProvider credentials, OAuth2Client oauth, IMailboxProvider mailboxes)
    {
        _accounts = accounts;
        _credentials = credentials;
        _oauth = oauth;
        _mailboxes = mailboxes;
    }

    /// <summary>Every declared account and whether it is ready, without touching the network.</summary>
    public async Task<IReadOnlyList<EmailAccountStatus>> ListAsync(CancellationToken cancellationToken)
    {
        var statuses = new List<EmailAccountStatus>();
        var defaultName = _accounts.DefaultName;
        foreach (var name in _accounts.Names)
        {
            var resolution = _accounts.Inspect(name);
            var isDefault = string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase);
            if (resolution.Account is not { } account)
            {
                statuses.Add(new EmailAccountStatus
                {
                    Name = name,
                    Provider = "?",
                    IsDefault = isDefault,
                    Ready = false,
                    Problem = string.Join("; ", resolution.Problems),
                });
                continue;
            }

            var problem = await _credentials.DiagnoseAsync(account, cancellationToken).ConfigureAwait(false);
            statuses.Add(new EmailAccountStatus
            {
                Name = name,
                Address = account.Address,
                Provider = account.Provider.ToString(),
                Reads = account.Incoming.ToString(),
                Sends = account.Outgoing?.ToString(),
                Rights = account.Rights.ToString(),
                Auth = account.Auth.Method.ToString(),
                IsDefault = isDefault,
                Ready = problem is null,
                Problem = problem,
            });
        }

        return statuses;
    }

    /// <summary>Signs an OAuth account in interactively and stores its tokens.</summary>
    public async Task LoginAsync(string account, IEmailLoginInteraction interaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        var resolved = _accounts.Resolve(account);
        var settings = resolved.Auth.OAuth
            ?? throw new EmailToolException(
                EmailErrorCode.InvalidRequest,
                $"E-mail account '{resolved.Name}' signs in with a password (Auth:PasswordEnvVar); there is nothing to log in to.");
        var secret = _credentials.ReadClientSecret(resolved);

        var tokens = settings.Flow == OAuthFlow.DeviceCode
            ? await DeviceSignInAsync(resolved, settings, secret, interaction, cancellationToken).ConfigureAwait(false)
            : await LoopbackSignInAsync(resolved, settings, secret, interaction, cancellationToken).ConfigureAwait(false);

        if (tokens.RefreshToken is null)
        {
            throw new EmailToolException(
                EmailErrorCode.LoginRequired,
                "The provider issued no refresh token, so the sign-in would expire within the hour. " +
                "For Microsoft, the application must allow the offline_access permission; for Google, revoke the app's access and log in again.");
        }

        await _credentials.StoreAsync(resolved, tokens, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Forgets an account's tokens; false when none were stored.</summary>
    public Task<bool> LogoutAsync(string account, CancellationToken cancellationToken)
    {
        var resolved = _accounts.Resolve(account);
        if (resolved.Auth.OAuth is null)
        {
            throw new EmailToolException(
                EmailErrorCode.InvalidRequest,
                $"E-mail account '{resolved.Name}' signs in with a password (Auth:PasswordEnvVar); there are no tokens to forget.");
        }

        return _credentials.ForgetAsync(resolved, cancellationToken);
    }

    /// <summary>Connects, authenticates and lists the folders of an account.</summary>
    public async Task<EmailCheckResult> CheckAsync(string account, CancellationToken cancellationToken)
    {
        var resolved = _accounts.Resolve(account);
        var folders = await _mailboxes.GetMailbox(resolved).ListFoldersAsync(cancellationToken).ConfigureAwait(false);
        var inbox = folders.FirstOrDefault(folder => folder.Role == FolderRoles.Inbox);
        return new EmailCheckResult
        {
            Account = resolved.Name,
            Folders = folders.Count,
            InboxTotal = inbox?.Total,
            InboxUnread = inbox?.Unread,
        };
    }

    private async Task<EmailTokenSet> DeviceSignInAsync(
        ResolvedEmailAccount account, OAuthSettings settings, string? secret, IEmailLoginInteraction interaction, CancellationToken cancellationToken)
    {
        var grant = await _oauth.RequestDeviceCodeAsync(settings, cancellationToken).ConfigureAwait(false);
        await interaction.ShowDeviceCodeAsync(account.Name, grant.VerificationUri, grant.UserCode, grant.ExpiresIn, cancellationToken).ConfigureAwait(false);
        return await _oauth.PollDeviceCodeAsync(settings, secret, grant, cancellationToken).ConfigureAwait(false);
    }

    private async Task<EmailTokenSet> LoopbackSignInAsync(
        ResolvedEmailAccount account, OAuthSettings settings, string? secret, IEmailLoginInteraction interaction, CancellationToken cancellationToken)
    {
        using var listener = LoopbackRedirectListener.Start();
        var session = PkceSession.Create();
        var authorization = OAuth2Client.BuildAuthorizationUri(settings, listener.RedirectUri, session, account.Address);
        await interaction.ShowAuthorizationUrlAsync(account.Name, authorization, cancellationToken).ConfigureAwait(false);

        using var race = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var viaBrowser = listener.WaitAsync(session.State, race.Token);
        // The paste prompt gets a thread of its own: a terminal read blocks its caller until a
        // line comes, whatever its signature (Console.In does), and the browser's redirect must
        // still end the sign-in while nobody types anything.
        var viaPaste = Task.Run(
            () => WaitForPastedRedirectAsync(account.Name, authorization, listener.RedirectUri, interaction, race.Token), CancellationToken.None);
        var first = await Task.WhenAny(viaBrowser, viaPaste).ConfigureAwait(false);
        var redirect = await first.ConfigureAwait(false);
        await race.CancelAsync().ConfigureAwait(false);

        // The state first: an address from an earlier attempt, or forged, is not this sign-in's
        // answer, whatever it says.
        if (!string.Equals(redirect.State, session.State, StringComparison.Ordinal))
            throw new EmailToolException(EmailErrorCode.AuthenticationFailed, "The redirect did not come from this sign-in (state mismatch); run the login again.");
        if (redirect.Error is { } error)
            throw new EmailToolException(EmailErrorCode.LoginRequired, $"The sign-in was refused: {error}.");
        if (redirect.Code is not { } code)
            throw new EmailToolException(EmailErrorCode.AuthenticationFailed, "The redirect carries no authorization code; run the login again.");

        return await _oauth.ExchangeCodeAsync(settings, secret, code, listener.RedirectUri, session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads pasted lines until one is an outcome of a sign-in. A line that is not is said to the
    /// interaction, with the reason, and the wait goes on: a rejected paste is not a failed
    /// sign-in. An empty line says nothing.
    /// </summary>
    private static async Task<AuthorizationRedirect> WaitForPastedRedirectAsync(
        string account, Uri authorization, Uri redirectUri, IEmailLoginInteraction interaction, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await interaction.ReadRedirectAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                // Nothing can be pasted here: only the browser redirect can finish the sign-in.
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            var pasted = Uri.TryCreate(line.Trim(), UriKind.Absolute, out var address) ? address : null;
            if (pasted is not null && AuthorizationRedirect.Parse(pasted) is { IsOutcome: true } redirect)
                return redirect;

            await interaction.ShowRedirectRejectedAsync(account, WhyRejected(pasted, authorization, redirectUri), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Why a pasted line is not the redirect of a sign-in, in one sentence that never repeats the
    /// line: it may hold an authorization code, a secret while it lives.
    /// </summary>
    private static string WhyRejected(Uri? pasted, Uri authorization, Uri redirectUri)
    {
        // A bare path is an absolute file address under Unix: only a web address is one here.
        if (pasted is null || (pasted.Scheme != Uri.UriSchemeHttp && pasted.Scheme != Uri.UriSchemeHttps))
            return "The pasted text is not an address.";
        if (string.Equals(pasted.Host, authorization.Host, StringComparison.OrdinalIgnoreCase))
            return "The pasted address is the one to open, not the one the browser ended on.";
        if (!pasted.IsLoopback)
            return $"The pasted address is not the redirect address of this sign-in ({redirectUri.AbsoluteUri}).";
        return "The pasted address carries no authorization code: it may be cut short.";
    }
}
