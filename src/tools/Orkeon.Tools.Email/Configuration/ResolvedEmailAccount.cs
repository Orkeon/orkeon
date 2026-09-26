namespace Orkeon.Tools.Email.Configuration;

/// <summary>A server a protocol client connects to.</summary>
/// <param name="Host">Host name or address.</param>
/// <param name="Port">TCP port.</param>
/// <param name="Security">Transport security.</param>
internal sealed record MailEndpoint(string Host, int Port, TransportSecurity Security);

/// <summary>OAuth 2.0 settings of an account, preset endpoints included.</summary>
internal sealed record OAuthSettings
{
    /// <summary>Client id of the registered application.</summary>
    public required string ClientId { get; init; }

    /// <summary>Environment variable holding the client secret, when the provider requires one.</summary>
    public string? ClientSecretEnvVar { get; init; }

    /// <summary>How the first token is obtained.</summary>
    public required OAuthFlow Flow { get; init; }

    /// <summary>Token endpoint (code exchange and refresh).</summary>
    public required Uri TokenEndpoint { get; init; }

    /// <summary>Authorization endpoint of the loopback flow.</summary>
    public Uri? AuthorizationEndpoint { get; init; }

    /// <summary>Device authorization endpoint of the device-code flow.</summary>
    public Uri? DeviceCodeEndpoint { get; init; }

    /// <summary>Scopes requested at sign-in and refresh.</summary>
    public required IReadOnlyList<string> Scopes { get; init; }

    /// <summary>
    /// Whether a refresh repeats the scopes. The Microsoft identity platform takes them on every
    /// token request; Google documents none on a refresh and keeps the ones consented to.
    /// </summary>
    public bool ScopesOnRefresh { get; init; }
}

/// <summary>How an account authenticates, once resolved.</summary>
internal sealed record ResolvedAuth
{
    /// <summary>The method.</summary>
    public required EmailAuthMethod Method { get; init; }

    /// <summary>Login name (the address unless overridden).</summary>
    public required string Username { get; init; }

    /// <summary>Environment variable holding the password (<see cref="EmailAuthMethod.Password"/>).</summary>
    public string? PasswordEnvVar { get; init; }

    /// <summary>OAuth settings (<see cref="EmailAuthMethod.OAuth2"/>).</summary>
    public OAuthSettings? OAuth { get; init; }
}

/// <summary>Guard rails applied to every message the account sends.</summary>
/// <param name="AllowedRecipients">Allowed recipient patterns; empty allows nobody.</param>
/// <param name="MaxRecipients">Most recipients per message, or null.</param>
/// <param name="MaxPerHour">Most messages per hour, or null.</param>
internal sealed record SendPolicy(IReadOnlyList<string> AllowedRecipients, int? MaxRecipients, int? MaxPerHour);

/// <summary>
/// An account after presets and validation: everything a backend needs, nothing left to guess.
/// </summary>
internal sealed record ResolvedEmailAccount
{
    /// <summary>The name agents address the account by.</summary>
    public required string Name { get; init; }

    /// <summary>The preset it started from.</summary>
    public required EmailProvider Provider { get; init; }

    /// <summary>The account's address.</summary>
    public required string Address { get; init; }

    /// <summary>Display name for <c>From</c>.</summary>
    public string? DisplayName { get; init; }

    /// <summary>What an agent may do.</summary>
    public required EmailRights Rights { get; init; }

    /// <summary>How mail is read.</summary>
    public required IncomingProtocol Incoming { get; init; }

    /// <summary>The reading server; null for Graph.</summary>
    public MailEndpoint? IncomingEndpoint { get; init; }

    /// <summary>How mail is sent, or null when the account has no sending side.</summary>
    public OutgoingProtocol? Outgoing { get; init; }

    /// <summary>The SMTP server; null for Graph or when the account does not send.</summary>
    public MailEndpoint? OutgoingEndpoint { get; init; }

    /// <summary>Authentication.</summary>
    public required ResolvedAuth Auth { get; init; }

    /// <summary>Sending guard rails.</summary>
    public required SendPolicy Send { get; init; }

    /// <summary>Whether a sent message is appended to the Sent folder by us.</summary>
    public required bool SaveSentCopy { get; init; }

    /// <summary>Protocol timeout, or null for the library default.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Whether the account grants <paramref name="right"/>.</summary>
    public bool Grants(EmailRights right) => (Rights & right) == right;
}
