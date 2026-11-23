using Orkeon.Tools.Abstractions.Auth;
using Orkeon.Tools.Chat.Places;

namespace Orkeon.Tools.Chat.Configuration;

/// <summary>An account after presets and validation: everything a provider needs, nothing left to guess.</summary>
internal sealed record ResolvedChatAccount
{
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

    /// <summary>The API base address of that cloud: the one host the account's token is sent to.</summary>
    public required Uri ApiBase { get; init; }

    /// <summary>OAuth settings, endpoints and scopes included.</summary>
    public required OAuthSettings OAuth { get; init; }

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
}
