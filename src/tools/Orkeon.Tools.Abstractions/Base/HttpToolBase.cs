using System.Reflection;
using Orkeon.Domain.Attributes;
using Orkeon.Domain.Tools.Security;
using Orkeon.Tools.Abstractions.Security;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Constants.Http;

namespace Orkeon.Tools.Abstractions.Base;

/// <summary>
/// Base class for tools that make HTTP requests.
/// Supports optional SSRF protection via IUrlValidator and header sanitization.
/// </summary>
public abstract class HttpToolBase : ToolBase
{
    /// <summary>
    /// Shared static HttpClient used as a fallback when no IHttpClientFactory is provided.
    /// Static HttpClient instances are safe to share and avoid socket exhaustion.
    /// </summary>
    private static readonly HttpClient s_sharedHttpClient = CreateSharedHttpClient();

    /// <summary>The underlying HTTP client used for outgoing requests.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1051", Justification = "Established protected base-class field referenced directly by derived HTTP tools across multiple projects; converting to a property would break the inherited contract without behavioral benefit.")]
    protected readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly IUrlValidator? _urlValidator;
    private readonly HttpHeaderSanitizer? _headerSanitizer;

    /// <inheritdoc />
    public override string Category
    {
        get
        {
            var contract = GetType().GetCustomAttribute<ToolContractAttribute>();
            return contract?.Category ?? "Web Operations";
        }
    }

    /// <summary>
    /// Constructor with IHttpClientFactory for proper socket management.
    /// Factory-managed clients are NOT disposed by this class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory for creating managed clients.</param>
    /// <param name="logger">Optional logger.</param>
    protected HttpToolBase(IHttpClientFactory httpClientFactory, ILogger? logger = null) : base(logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClient = httpClientFactory.CreateClient(GetType().Name);
        _ownsHttpClient = false; // Factory manages the lifecycle
        ConfigureHttpClient(_httpClient);
    }

    /// <summary>
    /// Constructor with IHttpClientFactory and SSRF protection.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory for creating managed clients.</param>
    /// <param name="urlValidator">URL validator for SSRF protection.</param>
    /// <param name="headerSanitizer">Header sanitizer to prevent header injection.</param>
    /// <param name="logger">Optional logger.</param>
    protected HttpToolBase(IHttpClientFactory httpClientFactory, IUrlValidator urlValidator, HttpHeaderSanitizer headerSanitizer, ILogger? logger = null)
        : this(httpClientFactory, logger)
    {
        _urlValidator = urlValidator;
        _headerSanitizer = headerSanitizer;
    }

    /// <summary>
    /// Legacy constructor for backward compatibility (no SSRF protection).
    /// When no HttpClient is provided, uses a shared static instance instead of creating a new one,
    /// to avoid socket exhaustion.
    /// </summary>
    protected HttpToolBase(HttpClient? httpClient = null, ILogger? logger = null) : base(logger)
    {
        if (httpClient != null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
            ConfigureHttpClient(_httpClient);
        }
        else
        {
            // Use shared static HttpClient to avoid socket exhaustion
            _httpClient = s_sharedHttpClient;
            _ownsHttpClient = false;
        }
    }

    /// <summary>
    /// Constructor with SSRF protection via URL validation and header sanitization.
    /// </summary>
    protected HttpToolBase(IUrlValidator urlValidator, HttpHeaderSanitizer headerSanitizer, HttpClient? httpClient = null, ILogger? logger = null)
        : this(httpClient, logger)
    {
        _urlValidator = urlValidator;
        _headerSanitizer = headerSanitizer;
    }

    /// <summary>
    /// Validates a URL against SSRF protection rules.
    /// If a full <see cref="IUrlValidator"/> is configured it is used; otherwise a
    /// <b>fail-closed</b> default guard applies — scheme is checked AND any host that is
    /// (or resolves to) a private/loopback/link-local/metadata address is denied. The
    /// validator never returns <see cref="UrlValidationResult.Allowed"/> on the sole basis
    /// of a valid scheme (no silent SSRF fail-open).
    /// </summary>
    protected Task<UrlValidationResult> ValidateUrlAsync(Uri url, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(url);
        return ValidateUrlCoreAsync();

        async Task<UrlValidationResult> ValidateUrlCoreAsync()
        {
            if (_urlValidator is not null)
                return await _urlValidator.ValidateUrlAsync(url, ct).ConfigureAwait(false);

            return await DefaultSsrfGuard.ValidateAsync(url, _logger, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sanitizes HTTP headers if a header sanitizer is configured.
    /// Returns null if no sanitizer is available.
    /// </summary>
    protected HeaderSanitizationResult? SanitizeHeaders(IDictionary<string, string> headers)
    {
        return _headerSanitizer?.SanitizeHeaders(headers);
    }

    private static void ConfigureHttpClient(HttpClient client)
    {
        try
        {
            client.Timeout = HttpDefaults.DefaultHttpTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpDefaults.DefaultUserAgent);
        }
        catch (InvalidOperationException)
        {
            // Client has already started a request; properties cannot be modified.
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "Ownership of the SocketsHttpHandler is transferred to the HttpClient via disposeHandler: true; the client disposes the handler when disposed. The only statement between handler construction and the client owning it is ConfigureHttpClient, which swallows its sole exception and cannot throw, so the handler cannot leak.")]
    private static HttpClient CreateSharedHttpClient()
    {
        // AllowAutoRedirect is disabled so a 3xx response cannot transparently
        // redirect an SSRF-validated request to a private/internal address that
        // bypassed the initial URL validation. Tools that need to follow a
        // redirect must re-validate and re-issue the request explicitly.
        // PooledConnectionLifetime forces periodic recycling of pooled connections so a
        // long-lived static HttpClient honours DNS changes (ANT-013). Prefer injecting an
        // IHttpClientFactory in production; this static client is only a fallback.
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };
        // disposeHandler: true transfers ownership of the handler to the HttpClient,
        // which disposes it when the client itself is disposed.
        var client = new HttpClient(handler, disposeHandler: true);
        ConfigureHttpClient(client);
        return client;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsHttpClient)
        {
            _httpClient?.Dispose();
        }
        base.Dispose(disposing);
    }
}
