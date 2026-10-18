namespace Orkeon.Tools.Abstractions.Auth;

/// <summary>How an OAuth 2.0 account obtains its first token.</summary>
internal enum OAuthFlow
{
    /// <summary>RFC 8628 device authorization grant (Microsoft).</summary>
    DeviceCode,

    /// <summary>Authorization code with PKCE and a loopback redirect (Google).</summary>
    LoopbackPkce,
}

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

/// <summary>The tokens one sign-in or one refresh returned, before a family stores them its own way.</summary>
internal sealed record OAuthTokenSet
{
    /// <summary>The bearer token sent to the service.</summary>
    public required string AccessToken { get; init; }

    /// <summary>The long-lived token that obtains new access tokens, when the provider issued one.</summary>
    public string? RefreshToken { get; init; }

    /// <summary>When <see cref="AccessToken"/> stops being accepted.</summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>The scopes the tokens were granted for.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];
}
