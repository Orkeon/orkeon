using System.Text.Json.Serialization;
using Orkeon.Tools.Abstractions.Auth;
using Orkeon.Tools.Chat.Accounts;
using Orkeon.Tools.Chat.Auth;
using Orkeon.Tools.Chat.Configuration;
using Orkeon.Tools.Chat.Constants;
using Orkeon.Tools.Chat.Places;
using Orkeon.Tools.Chat.Providers;

namespace Orkeon.Tools.Chat.Administration;

/// <summary>
/// One declared account, as <c>orkeon chat accounts</c> lists it — with <c>--json</c>, under the
/// names <c>chat_accounts</c> uses. No secret ever appears here.
/// </summary>
public sealed record ChatAccountStatus
{
    /// <summary>The account name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The chat service it opens (<c>Teams</c>, <c>TeamsWebhook</c>), or <c>?</c> when the declaration cannot be read.</summary>
    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    /// <summary>The rights it grants.</summary>
    [JsonPropertyName("rights")]
    public string? Rights { get; init; }

    /// <summary>Whether an application signs in as itself, and not a user.</summary>
    [JsonPropertyName("application")]
    public bool Application { get; init; }

    /// <summary>The Microsoft cloud it lives in; null for a webhook.</summary>
    [JsonPropertyName("cloud")]
    public string? Cloud { get; init; }

    /// <summary>Whether it is the account a call without <c>account</c> uses.</summary>
    [JsonPropertyName("default")]
    public bool IsDefault { get; init; }

    /// <summary>Whether it has everything it needs to connect (checked without network).</summary>
    [JsonPropertyName("ready")]
    public bool Ready { get; init; }

    /// <summary>What to fix, when it is not ready.</summary>
    [JsonPropertyName("problem")]
    public string? Problem { get; init; }

    /// <summary>What the configuration gets wrong, one sentence each; empty when the account resolves.</summary>
    [JsonPropertyName("configuration_problems")]
    public IReadOnlyList<string> ConfigurationProblems { get; init; } = [];

    /// <summary>Why a resolved account cannot sign in yet; null when it is ready or when it was set aside.</summary>
    [JsonPropertyName("credential_problem")]
    public string? CredentialProblem { get; init; }

    /// <summary>The OAuth token endpoint, which names the tenant; null for a webhook or an account set aside.</summary>
    [JsonPropertyName("token_endpoint")]
    public string? TokenEndpoint { get; init; }

    /// <summary>How many places the account may read. Read in-process; not part of the <c>--json</c> contract.</summary>
    [JsonIgnore]
    public int ReadPlaces { get; init; }

    /// <summary>How many places the account may post in. Read in-process; not part of the <c>--json</c> contract.</summary>
    [JsonIgnore]
    public int SendPlaces { get; init; }
}

/// <summary>What one right of an account can do now.</summary>
public sealed record ChatRightStatus
{
    /// <summary>The right.</summary>
    [JsonPropertyName("right")]
    public required string Right { get; init; }

    /// <summary>Whether everything the right asks for was granted at the sign-in.</summary>
    [JsonPropertyName("ready")]
    public required bool Ready { get; init; }

    /// <summary>What is missing, or what the right cannot do, one sentence each.</summary>
    [JsonPropertyName("missing")]
    public IReadOnlyList<string> Missing { get; init; } = [];
}

/// <summary>What <c>orkeon chat check</c> found by actually connecting.</summary>
public sealed record ChatCheckResult
{
    /// <summary>The account name.</summary>
    [JsonPropertyName("account")]
    public required string Account { get; init; }

    /// <summary>The signed-in user; null when no right of the account lets it be read.</summary>
    [JsonPropertyName("user")]
    public string? User { get; init; }

    /// <summary>Each right of the account, in order.</summary>
    [JsonPropertyName("rights")]
    public required IReadOnlyList<ChatRightStatus> Rights { get; init; }

    /// <summary>Whether the service was reached: a webhook is not, since only a post would tell. Read in-process; not part of the event contract.</summary>
    [JsonIgnore]
    public bool Contacted { get; init; } = true;

    /// <summary>Whether everything the account declares can be used.</summary>
    [JsonPropertyName("ready")]
    public bool Ready => Rights.All(right => right.Ready);
}

/// <summary>What became of a sign-out.</summary>
public sealed record ChatLogoutResult
{
    /// <summary>Whether tokens were stored, and are deleted.</summary>
    public required bool Deleted { get; init; }

    /// <summary>Whether the provider revoked the refresh token.</summary>
    public required bool Revoked { get; init; }

    /// <summary>Why the token was not revoked, and where its access is removed; null when it was.</summary>
    public string? Notice { get; init; }
}

/// <summary>
/// The operator's side of the chat family: list the accounts, sign an account in or out, and check
/// what it can do. Used by <c>orkeon chat</c>; never by an agent.
/// </summary>
public sealed class ChatAccountAdministration
{
    private readonly IChatAccountRegistry _accounts;
    private readonly ChatCredentialProvider _credentials;
    private readonly OAuth2Client _oauth;
    private readonly ChatSessions _sessions;

    internal ChatAccountAdministration(
        IChatAccountRegistry accounts, ChatCredentialProvider credentials, OAuth2Client oauth, ChatSessions sessions)
    {
        _accounts = accounts;
        _credentials = credentials;
        _oauth = oauth;
        _sessions = sessions;
    }

    /// <summary>Every declared account and whether it is ready, without touching the network.</summary>
    public async Task<IReadOnlyList<ChatAccountStatus>> ListAsync(CancellationToken cancellationToken)
    {
        var statuses = new List<ChatAccountStatus>();
        var defaultName = _accounts.DefaultName;
        foreach (var name in _accounts.Names)
        {
            var resolution = _accounts.Inspect(name);
            var isDefault = string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase);
            if (resolution.Account is not { } account)
            {
                statuses.Add(new ChatAccountStatus
                {
                    Name = name,
                    Provider = "?",
                    IsDefault = isDefault,
                    Ready = false,
                    Problem = string.Join("; ", resolution.Problems),
                    ConfigurationProblems = [.. resolution.Problems],
                });
                continue;
            }

            var problem = await _credentials.DiagnoseAsync(account, cancellationToken).ConfigureAwait(false);
            statuses.Add(new ChatAccountStatus
            {
                Name = name,
                Provider = account.Provider.ToString(),
                Rights = account.Rights.ToString(),
                Cloud = account.IsWebhook ? null : account.Cloud.ToString(),
                ReadPlaces = account.ReadPlaces.Count,
                // A webhook posts into the one place of its address.
                Application = account.IsApplication,
                SendPlaces = account.IsWebhook ? 1 : account.SendPlaces.Count,
                IsDefault = isDefault,
                Ready = problem is null,
                Problem = problem,
                CredentialProblem = problem,
                TokenEndpoint = account.IsWebhook ? null : account.OAuth.TokenEndpoint.AbsoluteUri,
            });
        }

        return statuses;
    }

    /// <summary>
    /// Signs an account in interactively and stores its tokens: a browser on a loopback redirect,
    /// or the device code when <paramref name="deviceCode"/>.
    /// </summary>
    public async Task LoginAsync(string account, bool deviceCode, IOAuthLoginInteraction interaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(interaction);
        var resolved = _accounts.Resolve(account);
        if (resolved.IsWebhook)
        {
            throw new ChatToolException(
                ChatErrorCode.NotSupportedByAccount,
                $"Chat account '{resolved.Name}' is a webhook: there is nothing to sign in. Its address comes from the environment variable {resolved.WebhookUrlEnvVar}.");
        }

        if (resolved.Application is { } application)
        {
            throw new ChatToolException(
                ChatErrorCode.NotSupportedByAccount,
                $"Chat account '{resolved.Name}' is an application: it signs in by itself, with the credential read by the environment variable {application.EnvVar}. Run `orkeon chat check {resolved.Name}`.");
        }

        ChatTokenSet tokens;
        try
        {
            tokens = ChatCredentialProvider.ToChat(await OAuthSignIn.RunAsync(
                _oauth, resolved.OAuth, deviceCode ? OAuthFlow.DeviceCode : OAuthFlow.LoopbackPkce, null, resolved.Name, loginHint: null, interaction, cancellationToken).ConfigureAwait(false));
        }
        catch (OAuthException ex)
        {
            throw ChatCredentialProvider.Translate(ex);
        }

        if (tokens.RefreshToken is null)
        {
            throw new ChatToolException(
                ChatErrorCode.ConsentMissing,
                "The provider issued no refresh token, so the sign-in would expire within the hour: the application must allow the offline_access permission.");
        }

        await _credentials.StoreAsync(resolved, tokens, cancellationToken).ConfigureAwait(false);
        _sessions.Forget(resolved);
    }

    /// <summary>
    /// Forgets an account's tokens, once the provider has been asked to revoke them when it can. A
    /// revocation that fails never keeps the tokens, and neither do settings that no longer resolve:
    /// the tokens of the account's earlier settings go too.
    /// </summary>
    public async Task<ChatLogoutResult> LogoutAsync(string account, CancellationToken cancellationToken)
    {
        var target = string.IsNullOrWhiteSpace(account) ? _accounts.DefaultName : account.Trim();
        if (target is null || _accounts.Names.Count == 0)
            target = _accounts.Resolve(account).Name;
        var resolved = _accounts.Inspect(target).Account;
        if (resolved is null && !ChatAccountResolver.IsValidName(target))
            resolved = _accounts.Resolve(target);

        var name = resolved?.Name ?? target;
        // A webhook has no token; those of earlier settings of the same name go all the same.
        if (resolved?.IsWebhook == true)
        {
            return new ChatLogoutResult
            {
                Deleted = await _credentials.ForgetAsync(name, account: null, CancellationToken.None).ConfigureAwait(false),
                Revoked = false,
                Notice = "The account is a webhook: there is nothing to revoke. Whoever made the workflow deletes it, or makes a new address.",
            };
        }

        // An application's token is in the memory of the process that asked for it, and lapses within the hour.
        if (resolved?.IsApplication == true)
        {
            var dropped = await _credentials.ForgetAsync(name, resolved, CancellationToken.None).ConfigureAwait(false);
            _sessions.Forget(resolved);
            return new ChatLogoutResult
            {
                Deleted = dropped,
                Revoked = false,
                Notice = "The account is an application: it signs in by itself and stores no token. An administrator withdraws its credential or its permissions.",
            };
        }

        var revoked = false;
        string? notice = null;
        var stored = resolved is null ? null : await _credentials.StoredAsync(resolved, cancellationToken).ConfigureAwait(false);
        if (resolved is not null && stored?.RefreshToken is { } refreshToken)
        {
            try
            {
                revoked = await _oauth.RevokeAsync(resolved.OAuth, null, refreshToken, cancellationToken).ConfigureAwait(false);
            }
            catch (OAuthException ex)
            {
                notice = ex.Message;
            }
            catch (OperationCanceledException)
            {
                notice = "the revocation was interrupted";
            }
        }

        // Not the caller's token: an interruption during the revocation must not keep the tokens.
        var deleted = await _credentials.ForgetAsync(name, resolved, CancellationToken.None).ConfigureAwait(false);
        if (resolved is not null)
            _sessions.Forget(resolved);
        if (!revoked)
            notice = notice is null ? ChatDefaults.RemoveAccessHint : $"The provider did not revoke the token: {notice}";
        return new ChatLogoutResult { Deleted = deleted, Revoked = revoked, Notice = notice };
    }

    /// <summary>Connects and tells, right by right, what the account can do now and what is missing.</summary>
    public async Task<ChatCheckResult> CheckAsync(string account, CancellationToken cancellationToken)
    {
        var resolved = _accounts.Resolve(account);
        if (resolved.IsWebhook)
            return await CheckWebhookAsync(resolved, cancellationToken).ConfigureAwait(false);
        if (resolved.IsApplication)
            return await CheckApplicationAsync(resolved, cancellationToken).ConfigureAwait(false);
        var session = _sessions.Get(resolved);
        var user = await UserAsync(session, cancellationToken).ConfigureAwait(false);
        var tokens = await _credentials.StoredAsync(resolved, cancellationToken).ConfigureAwait(false);
        var granted = (tokens?.Scopes ?? []).Select(scope => scope[(scope.LastIndexOf('/') + 1)..]).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rights = new List<ChatRightStatus>();
        foreach (var right in Enum.GetValues<ChatRights>().Where(right => right != ChatRights.None && resolved.Grants(right)))
        {
            var blocking = new List<string>();
            var notes = new List<string>();
            foreach (var row in ChatScopes.Table.Where(row => row.Right == right))
            {
                if (!resolved.IsGranted(row.Needs))
                    notes.Add($"{Served(row.Needs)} needs {row.Permission}: a tenant administrator grants it, then {row.Needs} goes into Auth:AdminGranted and the account signs in again");
                else if (!granted.Contains(row.Permission))
                    blocking.Add($"{row.Permission} was not granted at the sign-in: run `orkeon chat login {resolved.Name}`");
            }

            if (right is ChatRights.Read or ChatRights.Send)
                await CheckPlacesAsync(session, right, blocking, cancellationToken).ConfigureAwait(false);
            if (right is ChatRights.Edit or ChatRights.Delete or ChatRights.React or ChatRights.Meetings)
                notes.Add("no tool uses this right");

            rights.Add(new ChatRightStatus { Right = right.ToString(), Ready = blocking.Count == 0, Missing = [.. blocking, .. notes] });
        }

        return new ChatCheckResult { Account = resolved.Name, User = user, Rights = rights };
    }

    /// <summary>Sends nothing: what the workflow would answer can only be learnt by posting.</summary>
    private async Task<ChatCheckResult> CheckWebhookAsync(ResolvedChatAccount account, CancellationToken cancellationToken)
    {
        var problem = await _credentials.DiagnoseAsync(account, cancellationToken).ConfigureAwait(false);
        var send = new ChatRightStatus
        {
            Right = nameof(ChatRights.Send),
            Ready = problem is null,
            Missing = [problem ?? "nothing was sent: whether the workflow accepts a message is known at the first one"],
        };
        return new ChatCheckResult { Account = account.Name, Contacted = false, Rights = [send] };
    }

    /// <summary>
    /// Signs the application in and looks its places up. Its token does not say its permissions in a
    /// form to rely on: what no request of the check needs is told as such.
    /// </summary>
    private async Task<ChatCheckResult> CheckApplicationAsync(ResolvedChatAccount account, CancellationToken cancellationToken)
    {
        _ = await _credentials.GetAccessTokenAsync(account, cancellationToken).ConfigureAwait(false);
        var session = _sessions.Get(account);
        var rights = new List<ChatRightStatus>();
        if (account.Grants(ChatRights.Read))
        {
            var blocking = new List<string>();
            await CheckPlacesAsync(session, ChatRights.Read, blocking, cancellationToken).ConfigureAwait(false);
            rights.Add(new ChatRightStatus
            {
                Right = nameof(ChatRights.Read),
                Ready = blocking.Count == 0,
                Missing =
                [
                    .. blocking,
                    $"no message was read: a channel's need the application permission {ChatScopes.ChannelMessageRead}, its members {ChatScopes.ChannelMemberRead}, a chat's messages {ChatScopes.OfApplication(ChatScopes.ChatRead)}",
                ],
            });
        }

        if (account.Grants(ChatRights.Meetings))
            rights.Add(new ChatRightStatus { Right = nameof(ChatRights.Meetings), Ready = true, Missing = ["no tool uses this right"] });
        return new ChatCheckResult { Account = account.Name, Rights = rights };
    }

    /// <summary>The signed-in user, read only when a right of the account asks for the permission to.</summary>
    private async Task<string?> UserAsync(ChatSession session, CancellationToken cancellationToken)
    {
        if (session.Account.OAuth.Scopes.Contains(ChatScopes.UserRead, StringComparer.Ordinal))
        {
            try
            {
                var self = await session.Provider.GetSelfAsync(cancellationToken).ConfigureAwait(false);
                return self.Address ?? self.Name;
            }
            catch (ChatToolException ex) when (ex.Code == ChatErrorCode.ConsentMissing)
            {
                // The rights are checked one by one all the same, and say what is missing.
                return null;
            }
        }

        // Nothing else to ask: the sign-in itself is what can be checked.
        _ = await _credentials.GetAccessTokenAsync(session.Account, cancellationToken).ConfigureAwait(false);
        return null;
    }

    private static async Task CheckPlacesAsync(ChatSession session, ChatRights right, List<string> blocking, CancellationToken cancellationToken)
    {
        var use = right == ChatRights.Read ? PlaceUse.Read : PlaceUse.Send;
        var places = use == PlaceUse.Read ? session.Account.ReadPlaces : session.Account.SendPlaces;
        if (places.Count == 0)
        {
            blocking.Add($"{use}:AllowedPlaces is empty, which allows no place");
            return;
        }

        try
        {
            var rules = await session.Gate.RulesAsync(use, cancellationToken).ConfigureAwait(false);
            blocking.AddRange(rules.Problems.Select(problem => $"{use}:AllowedPlaces entry {problem}"));
            if (use == PlaceUse.Read)
                _ = await session.Provider.ListChatsAsync(1, null, cancellationToken).ConfigureAwait(false);
            if (session.Account.IsApplication)
                _ = await session.Provider.ListTeamsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChatToolException ex) when (ex.Code is ChatErrorCode.ConsentMissing or ChatErrorCode.InvalidRequest)
        {
            blocking.Add(ex.Message);
        }
    }

    private static string Served(ChatAdminGrants grant) => grant switch
    {
        ChatAdminGrants.ChannelMessages => "reading a channel's messages",
        ChatAdminGrants.ChannelMembers => "listing a channel's members",
        ChatAdminGrants.Meetings => "reading a transcript",
        _ => "acting in a channel",
    };
}
