using Orkeon.Tools.Abstractions.Auth;
using Orkeon.Tools.Chat.Places;

namespace Orkeon.Tools.Chat.Configuration;

/// <summary>What an application proves its identity with.</summary>
internal enum ChatApplicationCredential
{
    /// <summary>A PEM file with the certificate and its private key.</summary>
    Certificate,

    /// <summary>A file holding a token another identity provider issued.</summary>
    FederatedToken,

    /// <summary>A client secret.</summary>
    Secret,
}

/// <summary>The credential of an application account: its kind and the variable to read it by.</summary>
internal sealed record ChatApplication(ChatApplicationCredential Kind, string EnvVar);

/// <summary>An account after presets and validation: everything a provider needs, nothing left to guess.</summary>
internal sealed record ResolvedChatAccount
{
    private readonly Uri? _apiBase;
    private readonly OAuthSettings? _oauth;

    /// <summary>The name agents address the account by.</summary>
    public required string Name { get; init; }

    /// <summary>The chat service it opens.</summary>
    public required ChatProvider Provider { get; init; }

    /// <summary>What an agent may do.</summary>
    public required ChatRights Rights { get; init; }

    /// <summary>What the tenant administrator has consented to.</summary>
    public required ChatAdminGrants AdminGranted { get; init; }

    /// <summary>The Microsoft cloud.</summary>
    public required ChatCloud Cloud { get; init; }

    /// <summary>The API base address of that cloud: the one host the account's token is sent to. A webhook has none.</summary>
    public Uri ApiBase
    {
        get => _apiBase ?? throw NoGraph();
        init => _apiBase = value;
    }

    /// <summary>OAuth settings, endpoints and scopes included. A webhook has none.</summary>
    public OAuthSettings OAuth
    {
        get => _oauth ?? throw NoGraph();
        init => _oauth = value;
    }

    /// <summary>The variable that holds a webhook's address; null for an account of Microsoft Graph.</summary>
    public string? WebhookUrlEnvVar { get; init; }

    /// <summary>The credential of an account an application signs in with; null when a user signs in.</summary>
    public ChatApplication? Application { get; init; }

    /// <summary>Whether an application signs in as itself: nobody's chats, and no right to write.</summary>
    public bool IsApplication => Application is not null;

    /// <summary>Whether the account is a webhook: it posts into one place, and has no token.</summary>
    public bool IsWebhook => Provider == ChatProvider.TeamsWebhook;

    /// <summary>The places an agent may read; empty for none.</summary>
    public required IReadOnlyList<PlaceReference> ReadPlaces { get; init; }

    /// <summary>The places an agent may post in; empty for none.</summary>
    public required IReadOnlyList<PlaceReference> SendPlaces { get; init; }

    /// <summary>The most posts attempted in an hour, or null for no cap.</summary>
    public int? MaxPerHour { get; init; }

    /// <summary>Whether the account grants <paramref name="right"/>.</summary>
    public bool Grants(ChatRights right) => (Rights & right) == right;

    /// <summary>Whether the administrator has consented to <paramref name="grant"/>.</summary>
    public bool IsGranted(ChatAdminGrants grant) => (AdminGranted & grant) == grant;

    /// <summary>The name alone: the generated text would read what a webhook does not have.</summary>
    public override string ToString() => $"chat account '{Name}'";

    private InvalidOperationException NoGraph() => new($"Chat account '{Name}' does not call Microsoft Graph.");
}
