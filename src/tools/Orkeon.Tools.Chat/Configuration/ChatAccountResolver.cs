using Orkeon.Domain.SharedKernel.ValueObjects;
using Orkeon.Tools.Abstractions.Auth;
using Orkeon.Tools.Chat.Constants;
using Orkeon.Tools.Chat.Places;

namespace Orkeon.Tools.Chat.Configuration;

/// <summary>The outcome of resolving one declared account.</summary>
/// <param name="Account">The resolved account, or null when <paramref name="Problems"/> is not empty.</param>
/// <param name="Problems">Everything wrong with the declaration, each an actionable sentence.</param>
internal sealed record ChatAccountResolution(ResolvedChatAccount? Account, IReadOnlyList<string> Problems);

/// <summary>
/// Applies the provider presets to a declared account and validates the result. Every problem
/// is collected, not only the first, so an operator fixes a declaration in one pass.
/// </summary>
internal static class ChatAccountResolver
{
    private const int MaxNameLength = 64;

    private static readonly string[] GrantNames = Enum.GetNames<ChatAdminGrants>().Where(name => name != nameof(ChatAdminGrants.None)).ToArray();

    /// <summary>Resolves <paramref name="options"/>, declared under <paramref name="name"/>.</summary>
    public static ChatAccountResolution Resolve(string name, ChatAccountOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(options);

        var problems = new List<string>();
        if (!IsValidName(name))
            problems.Add("the account name may only hold letters, digits, '.', '_' and '-', starts with a letter or a digit, and has 64 characters at most");

        if (options.Provider is null)
            problems.Add("Provider is required: Teams");
        else if (options.Provider == ChatProvider.TeamsWebhook)
            problems.Add("Provider TeamsWebhook is not supported: write Teams");
        if (options.Rights == ChatRights.None)
            problems.Add("Rights is required and says what an agent may do, e.g. \"Rights\": \"Read, Send\"");
        if (options.Auth.Method == ChatAuthMethod.ClientCredentials)
            problems.Add("Auth:Method ClientCredentials is not supported: write OAuth2, or leave it out");

        var granted = ResolveGrants(options.Auth.AdminGranted, problems);
        var cloud = options.Cloud ?? ChatCloud.Global;
        var endpoints = ResolveCloud(cloud, problems);
        var oauth = endpoints is null ? null : ResolveOAuth(options, granted, endpoints.Value.Authority, problems);
        var read = ResolvePlaces(options.Read.AllowedPlaces, "Read:AllowedPlaces", problems);
        var send = ResolvePlaces(options.Send.AllowedPlaces, "Send:AllowedPlaces", problems);
        if (options.Send.MaxPerHour is <= 0)
            problems.Add("Send:MaxPerHour must be positive");

        // The problems name no account: whoever shows them says which (the registry's message,
        // the account list).
        if (problems.Count > 0 || options.Provider is null || oauth is null)
            return new ChatAccountResolution(null, problems);

        var account = new ResolvedChatAccount
        {
            Name = name,
            Provider = options.Provider.Value,
            Rights = options.Rights,
            AdminGranted = granted,
            Cloud = cloud,
            ApiBase = endpoints!.Value.Api,
            OAuth = oauth,
            ReadPlaces = read,
            SendPlaces = send,
            MaxPerHour = options.Send.MaxPerHour,
        };
        return new ChatAccountResolution(account, []);
    }

    /// <summary>Whether <paramref name="name"/> is usable as an account name (and a token file stem).</summary>
    public static bool IsValidName(string name) =>
        !string.IsNullOrEmpty(name)
        && name.Length <= MaxNameLength
        && char.IsAsciiLetterOrDigit(name[0])
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static ChatAdminGrants ResolveGrants(List<string> declared, List<string> problems)
    {
        var granted = ChatAdminGrants.None;
        foreach (var entry in declared.Where(entry => !string.IsNullOrWhiteSpace(entry)).Select(entry => entry.Trim()))
        {
            if (GrantNames.FirstOrDefault(name => string.Equals(name, entry, StringComparison.OrdinalIgnoreCase)) is { } known)
                granted |= Enum.Parse<ChatAdminGrants>(known);
            else
                problems.Add($"Auth:AdminGranted holds an entry that is not one of {string.Join(", ", GrantNames)}");
        }

        return granted;
    }

    private static (Uri Api, Uri Authority)? ResolveCloud(ChatCloud cloud, List<string> problems)
    {
        switch (cloud)
        {
            case ChatCloud.UsGov:
                return (new Uri("https://graph.microsoft.us/v1.0/"), new Uri("https://login.microsoftonline.us/"));
            case ChatCloud.UsGovDoD:
                return (new Uri("https://dod-graph.microsoft.us/v1.0/"), new Uri("https://login.microsoftonline.us/"));
            case ChatCloud.Global:
                return (new Uri("https://graph.microsoft.com/v1.0/"), new Uri("https://login.microsoftonline.com/"));
            default:
                problems.Add("Cloud China is not supported: Microsoft's pages give two different sign-in hosts for it");
                return null;
        }
    }

    private static OAuthSettings? ResolveOAuth(ChatAccountOptions options, ChatAdminGrants granted, Uri authorityHost, List<string> problems)
    {
        // Orkeon expands no variable in a chat setting: the problem names the field, never the value.
        if (SettingPlaceholder.TryMatch(options.Auth.ClientId, out _) || SettingPlaceholder.TryMatch(options.Auth.Tenant, out _))
        {
            problems.Add("Auth:ClientId or Auth:Tenant holds a variable reference, and Orkeon expands no variable here: write the id itself, it is not a secret");
            return null;
        }

        var clientId = options.Auth.ClientId?.Trim();
        if (string.IsNullOrEmpty(clientId))
        {
            problems.Add("Auth:ClientId is required (the id of your Microsoft Entra application)");
            return null;
        }

        var tenant = string.IsNullOrWhiteSpace(options.Auth.Tenant) ? ChatDefaults.DefaultTenant : options.Auth.Tenant.Trim();
        if (!char.IsAsciiLetterOrDigit(tenant[0]) || !tenant.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
        {
            problems.Add("Auth:Tenant is not a tenant id, domain or alias");
            return null;
        }

        if (string.Equals(tenant, "consumers", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("Auth:Tenant consumers is for personal accounts, which Microsoft Graph serves no Teams chat: write your organization's tenant id");
            return null;
        }

        var authority = new Uri(authorityHost, $"{tenant}/oauth2/v2.0/");
        return new OAuthSettings
        {
            ClientId = clientId,
            Flow = OAuthFlow.LoopbackPkce,
            TokenEndpoint = new Uri(authority, "token"),
            AuthorizationEndpoint = new Uri(authority, "authorize"),
            DeviceCodeEndpoint = new Uri(authority, "devicecode"),
            Scopes = [.. ChatScopes.Permissions(options.Rights, granted), ChatDefaults.OfflineAccessScope],
            ScopesOnRefresh = true,
            Prompt = ChatDefaults.SignInPrompt,
        };
    }

    private static List<PlaceReference> ResolvePlaces(List<string> declared, string key, List<string> problems)
    {
        var places = new List<PlaceReference>();
        foreach (var entry in declared.Where(entry => !string.IsNullOrWhiteSpace(entry)))
        {
            if (PlaceReference.TryParse(entry, out var place))
                places.Add(place);
            else
                problems.Add($"{key} entry '{entry.Trim()}' is not a place: {PlaceReference.Syntax}");
        }

        return places;
    }
}
