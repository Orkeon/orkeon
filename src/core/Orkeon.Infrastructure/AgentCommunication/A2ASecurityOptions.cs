using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// Security options for A2A protocol communications, including mTLS configuration.
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public class A2ASecurityOptions
{
    /// <summary>
    /// Path to the client certificate file for mTLS authentication.
    /// </summary>
    public string? ClientCertificatePath { get; set; }

    /// <summary>
    /// Password for the client certificate private key.
    /// </summary>
    public string? ClientCertificatePassword { get; set; }

    /// <summary>
    /// List of trusted certificate authority certificate paths.
    /// Client side: when set, only servers presenting certificates signed by these CAs
    /// are trusted (private-CA pinning). Server side: when <see cref="RequireMutualTls"/>
    /// is enabled, incoming client certificates must chain to one of these CAs
    /// (unless pinned via <see cref="TrustedClientCertificateThumbprints"/>).
    /// </summary>
    public Collection<string> TrustedCertificateAuthorities { get; } = [];

    /// <summary>
    /// Thumbprints (hex, case-insensitive) of client certificates accepted by the server
    /// when <see cref="RequireMutualTls"/> is enabled — exact pinning, checked before the
    /// <see cref="TrustedCertificateAuthorities"/> chain validation. Pinned certificates
    /// are still rejected outside their validity window.
    /// </summary>
    public Collection<string> TrustedClientCertificateThumbprints { get; } = [];

    /// <summary>
    /// Whether mTLS is required for server-to-server communication. When enabled, the
    /// server requires a client certificate that chains to one of the
    /// <see cref="TrustedCertificateAuthorities"/> or matches one of the
    /// <see cref="TrustedClientCertificateThumbprints"/>; starting the server without any
    /// configured trust anchor throws (fail-closed) — mere presence and date validity of
    /// a certificate is not authentication.
    /// </summary>
    public bool RequireMutualTls { get; set; }

    /// <summary>
    /// Authentication schemes the server accepts on its task endpoints: <c>Bearer</c> and/or
    /// <c>ApiKey</c>. Empty means no authentication is required. Each declared scheme is
    /// validated, not merely matched (<see cref="A2ACredentialValidator"/>): a bearer token by a
    /// registered <c>IAuthenticationProvider</c> (<c>A2A:Security:AzureAD</c> /
    /// <c>A2A:Security:Oidc</c>), an API key against <see cref="ApiKeySecretNames"/>. The server
    /// refuses to start when a declared scheme has no validator.
    /// </summary>
    public Collection<string> AllowedAuthSchemes { get; } = [];

    /// <summary>
    /// Names of the secrets holding the API keys the <c>ApiKey</c> scheme accepts
    /// (<c>Authorization: ApiKey &lt;key&gt;</c>). Each name is read through the
    /// <c>ISecretProvider</c> on every request — <c>ORKEON_&lt;NAME&gt;</c> with the default
    /// chain — so keys never sit in the configuration file and rotate without a restart.
    /// </summary>
    public Collection<string> ApiKeySecretNames { get; } = [];
}
