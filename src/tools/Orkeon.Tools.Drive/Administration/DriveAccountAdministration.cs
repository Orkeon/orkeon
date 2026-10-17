using System.Text.Json.Serialization;
using Orkeon.Tools.Abstractions.Auth;
using Orkeon.Tools.Drive.Accounts;
using Orkeon.Tools.Drive.Auth;
using Orkeon.Tools.Drive.Drives;

namespace Orkeon.Tools.Drive.Administration;

/// <summary>
/// One declared account, as <c>orkeon drive accounts</c> lists it — with <c>--json</c>, under the
/// names <c>drive_accounts</c> uses. No secret ever appears here.
/// </summary>
public sealed record DriveAccountStatus
{
    /// <summary>The account name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The drive it opens (<c>GoogleDrive</c>, <c>OneDrive</c>), or <c>?</c> when the declaration cannot be read.</summary>
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    /// <summary>The rights it grants.</summary>
    [JsonPropertyName("rights")]
    public string? Rights { get; init; }

    /// <summary>The folder it is confined to, <c>/</c> for the whole drive.</summary>
    [JsonPropertyName("root")]
    public string? Root { get; init; }

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
    /// How many recipient patterns the account may share with (0: none). Read in-process
    /// (DriveRightsCheck); not part of the <c>--json</c> contract.
    /// </summary>
    [JsonIgnore]
    public int AllowedRecipients { get; init; }

    /// <summary>Whether the account may create public links. Read in-process; not part of the <c>--json</c> contract.</summary>
    [JsonIgnore]
    public bool AllowPublicLinks { get; init; }
}

/// <summary>What <c>orkeon drive check</c> found by actually connecting.</summary>
public sealed record DriveCheckResult
{
    /// <summary>The account name.</summary>
    public required string Account { get; init; }

    /// <summary>The root folder's name as the provider reports it.</summary>
    public required string RootName { get; init; }

    /// <summary>The root folder's path, <c>/</c> for the whole drive.</summary>
    public required string RootPath { get; init; }

    /// <summary>How many items the first page of the root folder listed.</summary>
    public required int Items { get; init; }

    /// <summary>Whether the root folder holds more than that first page.</summary>
    public required bool More { get; init; }
}

/// <summary>
/// The operator's side of the drive family: list the accounts, sign an account in or out, and
/// check that an account connects. Used by <c>orkeon drive</c>; never by an agent.
/// </summary>
public sealed class DriveAccountAdministration
{
    private const int CheckPage = 10;

    private readonly IDriveAccountRegistry _accounts;
    private readonly DriveCredentialProvider _credentials;
    private readonly OAuth2Client _oauth;
    private readonly IDriveFactory _drives;

    internal DriveAccountAdministration(
        IDriveAccountRegistry accounts, DriveCredentialProvider credentials, OAuth2Client oauth, IDriveFactory drives)
    {
        _accounts = accounts;
        _credentials = credentials;
        _oauth = oauth;
        _drives = drives;
    }

    /// <summary>Every declared account and whether it is ready, without touching the network.</summary>
    public async Task<IReadOnlyList<DriveAccountStatus>> ListAsync(CancellationToken cancellationToken)
    {
        var statuses = new List<DriveAccountStatus>();
        var defaultName = _accounts.DefaultName;
        foreach (var name in _accounts.Names)
        {
            var resolution = _accounts.Inspect(name);
            var isDefault = string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase);
            if (resolution.Account is not { } account)
            {
                statuses.Add(new DriveAccountStatus
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
            statuses.Add(new DriveAccountStatus
            {
                Name = name,
                Provider = account.Provider.ToString(),
                Rights = account.Rights.ToString(),
                Root = account.RootPath,
                AllowedRecipients = account.Share.AllowedRecipients.Count,
                AllowPublicLinks = account.Share.AllowPublicLinks,
                IsDefault = isDefault,
                Ready = problem is null,
                Problem = problem,
            });
        }

        return statuses;
    }

    /// <summary>Signs an account in interactively and stores its tokens.</summary>
    public async Task LoginAsync(string account, IOAuthLoginInteraction interaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        var resolved = _accounts.Resolve(account);
        var secret = _credentials.ReadClientSecret(resolved);

        DriveTokenSet tokens;
        try
        {
            tokens = DriveCredentialProvider.ToDrive(await OAuthSignIn.RunAsync(
                _oauth, resolved.OAuth, secret, resolved.Name, loginHint: null, interaction, cancellationToken).ConfigureAwait(false));
        }
        catch (OAuthException ex)
        {
            throw DriveCredentialProvider.Translate(ex);
        }

        if (tokens.RefreshToken is null)
        {
            throw new DriveToolException(
                DriveErrorCode.LoginRequired,
                "The provider issued no refresh token, so the sign-in would expire within the hour. " +
                "For Microsoft, the application must allow the offline_access permission; for Google, revoke the app's access and log in again.");
        }

        await _credentials.StoreAsync(resolved, tokens, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Forgets an account's tokens; false when none were stored.</summary>
    public Task<bool> LogoutAsync(string account, CancellationToken cancellationToken)
    {
        var resolved = _accounts.Resolve(account);
        return _credentials.ForgetAsync(resolved, cancellationToken);
    }

    /// <summary>Connects, finds the account's root folder and lists its first items.</summary>
    public async Task<DriveCheckResult> CheckAsync(string account, CancellationToken cancellationToken)
    {
        var resolved = _accounts.Resolve(account);
        var drive = _drives.GetDrive(resolved);
        var root = await drive.GetRootAsync(cancellationToken).ConfigureAwait(false);
        var page = await drive.ListAsync(root.Id, CheckPage, cursor: null, cancellationToken).ConfigureAwait(false);
        return new DriveCheckResult
        {
            Account = resolved.Name,
            RootName = root.Name,
            RootPath = resolved.RootPath,
            Items = page.Items.Count,
            More = page.Cursor is not null,
        };
    }
}
