using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// What an HTTP listener of the framework asks of a caller, whatever it serves (FWK-14): the
/// authentication schemes and the mutual-TLS trust. The server-side half of
/// <see cref="A2ASecurityOptions"/>, neutral so that <c>orkeon-host</c>'s run API enforces the very rules
/// the A2A server does, through the same <see cref="HttpRequestAuthenticator"/>.
/// </summary>
[Experimental("ORKEXP006", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed class HttpSecurityOptions
{
    /// <summary>
    /// Authentication schemes accepted: <c>Bearer</c> and/or <c>ApiKey</c>. Empty means no authentication is
    /// required. Each declared scheme is validated, not merely matched (see <see cref="A2ACredentialValidator"/>).
    /// </summary>
    public Collection<string> AllowedAuthSchemes { get; } = [];

    /// <summary>
    /// Names of the secrets holding the API keys the <c>ApiKey</c> scheme accepts, read through the
    /// <c>ISecretProvider</c> on every request.
    /// </summary>
    public Collection<string> ApiKeySecretNames { get; } = [];

    /// <summary>Whether a client certificate chaining to a trust anchor is required (fail-closed without one).</summary>
    public bool RequireMutualTls { get; set; }

    /// <summary>Paths of the CA certificates a client certificate must chain to when mutual TLS is required.</summary>
    public Collection<string> TrustedCertificateAuthorities { get; } = [];

    /// <summary>Thumbprints of client certificates pinned exactly (still refused outside their validity window).</summary>
    public Collection<string> TrustedClientCertificateThumbprints { get; } = [];

    /// <summary>The server-side rules of <paramref name="a2a"/>, as an independent copy.</summary>
    /// <param name="a2a">The A2A security options.</param>
    public static HttpSecurityOptions From(A2ASecurityOptions a2a)
    {
        ArgumentNullException.ThrowIfNull(a2a);
        var copy = new HttpSecurityOptions { RequireMutualTls = a2a.RequireMutualTls };
        foreach (var scheme in a2a.AllowedAuthSchemes) copy.AllowedAuthSchemes.Add(scheme);
        foreach (var name in a2a.ApiKeySecretNames) copy.ApiKeySecretNames.Add(name);
        foreach (var ca in a2a.TrustedCertificateAuthorities) copy.TrustedCertificateAuthorities.Add(ca);
        foreach (var thumbprint in a2a.TrustedClientCertificateThumbprints) copy.TrustedClientCertificateThumbprints.Add(thumbprint);
        return copy;
    }
}
