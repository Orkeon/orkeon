using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Application.Interfaces.Security;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>The verdict of <see cref="HttpRequestAuthenticator.AuthorizeAsync"/>.</summary>
[Experimental("ORKEXP006", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed class HttpAuthorization
{
    /// <summary>Initializes a verdict.</summary>
    /// <param name="allowed">Whether the request may proceed.</param>
    /// <param name="statusCode">The status to answer with when it may not (401 or 403); 200 otherwise.</param>
    /// <param name="error">The message for the caller; never contains a credential.</param>
    /// <param name="reason">The detail for the log.</param>
    public HttpAuthorization(bool allowed, int statusCode, string? error, string? reason)
    {
        Allowed = allowed;
        StatusCode = statusCode;
        Error = error;
        Reason = reason;
    }

    /// <summary>The request may proceed.</summary>
    public static HttpAuthorization Allow { get; } = new(true, 200, null, null);

    /// <summary>Whether the request may proceed.</summary>
    public bool Allowed { get; }

    /// <summary>The status to answer with when it may not (401 or 403); 200 otherwise.</summary>
    public int StatusCode { get; }

    /// <summary>The message for the caller; never contains a credential.</summary>
    public string? Error { get; }

    /// <summary>The detail for the log.</summary>
    public string? Reason { get; }
}

/// <summary>
/// Applies <see cref="HttpSecurityOptions"/> to the requests of an <see cref="HttpListener"/>: the mutual-TLS
/// client trust (SEC-012) then the <c>Authorization</c> header against its declared schemes (GAP-09). Extracted
/// from <c>A2AServer</c> (FWK-14) so every HTTP surface of the framework enforces one set of rules.
/// </summary>
[Experimental("ORKEXP006", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public sealed partial class HttpRequestAuthenticator : IDisposable
{
    private readonly HttpSecurityOptions _security;
    private readonly A2ACredentialValidator _credentials;
    private readonly ILogger _logger;
    private HashSet<string>? _trustedClientThumbprints;
    private readonly List<X509Certificate2> _trustedClientCertificateAuthorities = [];

    /// <summary>Initializes a new instance of <see cref="HttpRequestAuthenticator"/>.</summary>
    /// <param name="security">The rules to apply.</param>
    /// <param name="bearerProviders">The validators a <c>Bearer</c> token is offered to.</param>
    /// <param name="secretProvider">The secret provider the <c>ApiKey</c> keys are read from.</param>
    /// <param name="logger">Optional logger.</param>
    /// <param name="sectionName">The configuration section the rules came from, named in the start refusals.</param>
    public HttpRequestAuthenticator(
        HttpSecurityOptions security,
        IEnumerable<IAuthenticationProvider>? bearerProviders = null,
        ISecretProvider? secretProvider = null,
        ILogger? logger = null,
        string sectionName = A2ACredentialValidator.DefaultSection)
    {
        ArgumentNullException.ThrowIfNull(security);
        _security = security;
        _credentials = new A2ACredentialValidator(security, bearerProviders ?? [], secretProvider, sectionName);
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>Whether any rule is declared; when none is, every request is let through.</summary>
    public bool IsRequired => _credentials.IsRequired || _security.RequireMutualTls;

    /// <summary>
    /// Fail-closed start check: a declared scheme without a validator, or mutual TLS without a trust anchor,
    /// throws <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task EnsureReadyAsync(CancellationToken ct = default)
    {
        await _credentials.EnsureReadyAsync(ct).ConfigureAwait(false);
        if (_security.RequireMutualTls)
            InitializeMutualTlsTrust();
    }

    /// <summary>
    /// Applies the rules to a request: 403 when mutual TLS is required and the client certificate is missing
    /// or untrusted, 401 when a credential is required and refused.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<HttpAuthorization> AuthorizeAsync(HttpListenerContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_security.RequireMutualTls && TryGetClientCertificate(context) == null)
        {
            return new HttpAuthorization(false, 403, "Client certificate required (mTLS)",
                "missing or untrusted client certificate (mTLS required)");
        }

        if (_credentials.IsRequired)
        {
            var validation = await _credentials.ValidateAsync(context.Request.Headers["Authorization"], ct).ConfigureAwait(false);
            if (!validation.IsValid)
                return new HttpAuthorization(false, 401, "Authentication required", validation.Error ?? "credential rejected");
        }

        return HttpAuthorization.Allow;
    }

    private X509Certificate2? TryGetClientCertificate(HttpListenerContext context)
    {
        try
        {
            var cert = context.Request.GetClientCertificate();
            if (cert == null)
                return null;

            // SEC-012: presence + date validity is not authentication: the certificate
            // must chain to a configured CA or match a pinned thumbprint.
            return IsClientCertificateTrusted(cert) ? cert : null;
        }
        catch (HttpListenerException)
        {
            return null;
        }
    }

    /// <summary>
    /// Materializes the mTLS client trust; throws when mutual TLS is required but no usable trust anchor
    /// exists (fail-closed: accepting any date-valid certificate is not mutual authentication).
    /// </summary>
    public void InitializeMutualTlsTrust()
    {
        _trustedClientThumbprints = new HashSet<string>(
            _security.TrustedClientCertificateThumbprints.Where(t => !string.IsNullOrWhiteSpace(t)),
            StringComparer.OrdinalIgnoreCase);

        DisposeTrustedClientCas();
        foreach (var path in _security.TrustedCertificateAuthorities)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            try
            {
                // OUT-OF-SCOPE: trusted CA bundle on host, not VFS-mounted user data
                _trustedClientCertificateAuthorities.Add(X509CertificateLoader.LoadCertificateFromFile(path));
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
            {
                LogTrustedCaLoadFailed(ex, path);
            }
        }

        if (_security.RequireMutualTls
            && _trustedClientThumbprints.Count == 0
            && _trustedClientCertificateAuthorities.Count == 0)
        {
            throw new InvalidOperationException(
                "RequireMutualTls is enabled but no usable client trust is configured: set " +
                "TrustedCertificateAuthorities (CA certificate paths) and/or " +
                "TrustedClientCertificateThumbprints. Without a trust anchor every date-valid " +
                "certificate would be accepted, which is not mutual authentication. For local " +
                "development without client authentication, disable RequireMutualTls.");
        }

        LogMutualTlsTrustInitialized(_trustedClientCertificateAuthorities.Count, _trustedClientThumbprints.Count);
    }

    /// <summary>
    /// Whether <paramref name="certificate"/> matches a pinned thumbprint (within its validity window) or chains
    /// to one of the trusted CAs (custom root trust: an unpinned self-signed certificate is refused). Revocation
    /// is not checked: the trust anchors are private CAs without CRL/OCSP endpoints (documented limitation).
    /// </summary>
    /// <param name="certificate">The client certificate.</param>
    public bool IsClientCertificateTrusted(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        if (_trustedClientThumbprints is { Count: > 0 }
            && _trustedClientThumbprints.Contains(certificate.Thumbprint))
        {
            var nowUtc = DateTime.UtcNow;
            return certificate.NotBefore.ToUniversalTime() <= nowUtc
                && nowUtc <= certificate.NotAfter.ToUniversalTime();
        }

        if (_trustedClientCertificateAuthorities.Count == 0)
            return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        foreach (var ca in _trustedClientCertificateAuthorities)
            chain.ChainPolicy.CustomTrustStore.Add(ca);

        return chain.Build(certificate);
    }

    /// <summary>Releases the CA certificates.</summary>
    public void Dispose() => DisposeTrustedClientCas();

    private void DisposeTrustedClientCas()
    {
        foreach (var ca in _trustedClientCertificateAuthorities)
            ca.Dispose();
        _trustedClientCertificateAuthorities.Clear();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "HTTP mTLS client trust initialized: {CaCount} CA(s) loaded, {ThumbprintCount} pinned thumbprint(s)")]
    private partial void LogMutualTlsTrustInitialized(int caCount, int thumbprintCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "HTTP trusted CA could not be loaded and is ignored: {Path}")]
    private partial void LogTrustedCaLoadFailed(Exception ex, string path);
}
