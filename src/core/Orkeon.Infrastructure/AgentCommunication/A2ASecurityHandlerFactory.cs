using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.FileSystem;
using System.Diagnostics.CodeAnalysis;

namespace Orkeon.Infrastructure.AgentCommunication;

/// <summary>
/// Builds a <see cref="SocketsHttpHandler"/> configured from <see cref="A2ASecurityOptions"/>.
/// Wires the client certificate (mTLS), server-certificate validation, and trusted
/// certificate authorities so the A2A HTTP client honours the declared security posture
/// instead of issuing plain, unauthenticated HTTP. The handler pools and reuses
/// connections (<see cref="SocketsHttpHandler.PooledConnectionLifetime"/>) — it is meant
/// to be built once and shared across calls (ANT-018), not created per request.
/// </summary>
[Experimental("ORKEXP001", UrlFormat = "https://github.com/Orkeon/orkeon/blob/main/docs/reference/experimental-apis.md")]
public static partial class A2ASecurityHandlerFactory
{
    /// <summary>
    /// Creates a <see cref="SocketsHttpHandler"/> reflecting <paramref name="security"/>.
    /// When <see cref="A2ASecurityOptions.ClientCertificatePath"/> is set the client certificate
    /// is loaded through the VFS (<paramref name="fileSystem"/>) and presented to the server.
    /// The server certificate is always validated — by the OS trust store by default, or pinned to
    /// <see cref="A2ASecurityOptions.TrustedCertificateAuthorities"/> when configured. There is no
    /// way to disable validation; a self-signed or local-dev server must pin its CA.
    /// Disposing the handler does not dispose the imported client certificate — the owner
    /// must dispose the entries of <c>SslOptions.ClientCertificates</c> explicitly.
    /// </summary>
    /// <param name="security">The A2A security options (may be <c>null</c> → default handler).</param>
    /// <param name="fileSystem">VFS service used to load certificate bytes (mount-aware, rights-audited).</param>
    /// <param name="logger">Optional logger receiving the security warning when validation is opted out.</param>
    /// <param name="ct">Cancellation token for certificate loading.</param>
    public static Task<SocketsHttpHandler> CreateAsync(
        A2ASecurityOptions? security,
        IFileSystemService fileSystem,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        return CreateCoreAsync();

        async Task<SocketsHttpHandler> CreateCoreAsync()
        {
            // 2-minute pooled-connection recycling: repo convention (cf. HttpToolBase,
            // SseMcpTransport) so long-lived handlers still honour DNS changes.
            SocketsHttpHandler? handler = null;
            try
            {
                handler = new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(2)
                };

                if (security != null)
                {
                    await ConfigureClientCertificateAsync(handler, security, fileSystem, ct).ConfigureAwait(false);
                    ConfigureServerValidation(handler, security, logger);
                }

                var configured = handler;
                handler = null; // ownership transferred to the caller; suppress the finally dispose
                return configured;
            }
            finally
            {
                // Disposes only if configuration threw before ownership was handed back to the
                // caller, so the handler can never leak.
                handler?.Dispose();
            }
        }
    }

    private static async Task ConfigureClientCertificateAsync(
        SocketsHttpHandler handler,
        A2ASecurityOptions security,
        IFileSystemService fileSystem,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(security.ClientCertificatePath))
            return;

        var certBytes = await fileSystem
            .TryReadAllBytesAsync(security.ClientCertificatePath, ct)
            .ConfigureAwait(false)
            ?? throw new FileNotFoundException(
                "A2A client certificate not found at the configured virtual path.");

        var certificate = LoadClientCertificate(certBytes, security.ClientCertificatePassword);

        handler.SslOptions.ClientCertificates ??= new X509CertificateCollection();
        handler.SslOptions.ClientCertificates.Add(certificate);
    }

    private static void ConfigureServerValidation(SocketsHttpHandler handler, A2ASecurityOptions security, ILogger? logger)
    {
        // The server certificate is always validated. With no configured CAs the OS trust store
        // applies (no custom callback); when TrustedCertificateAuthorities is set, trust is pinned to
        // those CAs. There is deliberately NO accept-any-certificate opt-out — a self-signed or
        // local-dev server must pin its CA via TrustedCertificateAuthorities.
        if (security.TrustedCertificateAuthorities.Count == 0)
            return;

        var trustedThumbprints = LoadTrustedThumbprints(security.TrustedCertificateAuthorities);
        handler.SslOptions.RemoteCertificateValidationCallback =
            (_, cert, chain, errors) => ValidateAgainstTrustedCas(cert as X509Certificate2, chain, errors, trustedThumbprints);

        if (logger != null)
            LogServerCertificatePinned(logger, trustedThumbprints.Count);
    }

    /// <summary>
    /// Private-CA pinning: vouches only for an unknown chain (<see
    /// cref="SslPolicyErrors.RemoteCertificateChainErrors"/> — expected when the CA is absent
    /// from the OS store). Any other TLS error — host-name mismatch, missing certificate —
    /// is never bypassed: the pin list asserts who signed the certificate, not which host
    /// presented it (SEC-011 hardening).
    /// </summary>
    internal static bool ValidateAgainstTrustedCas(
        X509Certificate2? cert,
        X509Chain? chain,
        SslPolicyErrors errors,
        IReadOnlySet<string> trustedThumbprints)
    {
        if (errors == SslPolicyErrors.None)
            return true;

        if ((errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
            return false;

        if (cert == null)
            return false;

        // Accept if the presented certificate (or any chain element) is a configured trusted CA.
        if (trustedThumbprints.Contains(cert.Thumbprint))
            return true;

        if (chain == null)
            return false;

        return chain.ChainElements.Any(element => trustedThumbprints.Contains(element.Certificate.Thumbprint));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "A2A server-certificate validation pinned to {Count} trusted certificate authority/authorities")]
    private static partial void LogServerCertificatePinned(ILogger logger, int count);

    private static HashSet<string> LoadTrustedThumbprints(IReadOnlyList<string> caPaths)
    {
        var thumbprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in caPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            try
            {
                using var ca = X509CertificateLoader.LoadCertificateFromFile(path); // OUT-OF-SCOPE: trusted CA bundle on host, not VFS-mounted user data
                thumbprints.Add(ca.Thumbprint);
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException or UnauthorizedAccessException)
            {
                // Skip unreadable/invalid CA entries — pinning simply ignores them.
            }
        }

        return thumbprints;
    }

    private static X509Certificate2 LoadClientCertificate(byte[] certBytes, string? password)
    {
        return string.IsNullOrEmpty(password)
            ? X509CertificateLoader.LoadPkcs12(certBytes, password: null)
            : X509CertificateLoader.LoadPkcs12(certBytes, password);
    }
}
