using System.Collections.ObjectModel;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// Security options for A2A protocol communications, including mTLS configuration.
/// </summary>
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
    /// Allowed authentication schemes for incoming requests (e.g., "Bearer", "ApiKey").
    /// Empty means no authentication is required.
    /// </summary>
    public Collection<string> AllowedAuthSchemes { get; } = [];
}
