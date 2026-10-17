using System.Text.Json.Serialization;
using Orkeon.Tools.Abstractions.Auth;
using Orkeon.Tools.Email.Accounts;
using Orkeon.Tools.Email.Auth;
using Orkeon.Tools.Email.Configuration;
using Orkeon.Tools.Email.Mailboxes;
using Orkeon.Tools.Email.Trial;

namespace Orkeon.Tools.Email.Administration;

/// <summary>
/// How an interactive e-mail sign-in talks to the person at the terminal: the shared
/// <see cref="IOAuthLoginInteraction"/>, under the name the e-mail family has always published.
/// </summary>
#pragma warning disable CA1040 // Kept, empty, so that implementers written for the e-mail family still compile.
public interface IEmailLoginInteraction : IOAuthLoginInteraction;
#pragma warning restore CA1040

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

    /// <summary>
    /// How many recipients the account may send to (0: none, so no send). Read in-process
    /// (EmailRightsCheck); not part of the <c>--json</c> contract.
    /// </summary>
    [JsonIgnore]
    public int AllowedRecipients { get; init; }
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

            // The trial account has no credential to look for: it is ready by construction.
            var problem = ReferenceEquals(account, EmailTrial.Account)
                ? null
                : await _credentials.DiagnoseAsync(account, cancellationToken).ConfigureAwait(false);
            statuses.Add(new EmailAccountStatus
            {
                Name = name,
                Address = account.Address,
                Provider = account.Provider.ToString(),
                Reads = account.Incoming.ToString(),
                Sends = account.Outgoing?.ToString(),
                Rights = account.Rights.ToString(),
                AllowedRecipients = account.Send.AllowedRecipients.Count,
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

        EmailTokenSet tokens;
        try
        {
            tokens = EmailCredentialProvider.ToEmail(await OAuthSignIn.RunAsync(
                _oauth, settings, secret, resolved.Name, resolved.Address, interaction, cancellationToken).ConfigureAwait(false));
        }
        catch (OAuthException ex)
        {
            throw EmailCredentialProvider.Translate(ex);
        }

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
}
