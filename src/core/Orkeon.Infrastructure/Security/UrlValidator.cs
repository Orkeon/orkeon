using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using Orkeon.Domain.Tools.Security;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Infrastructure.Security;

/// <summary>
/// Validates URLs to prevent SSRF attacks by checking schemes, hosts, ports,
/// IP ranges, and domain allow/block lists.
/// </summary>
public sealed partial class UrlValidator : IUrlValidator
{
    private readonly UrlSecurityOptions _options;
    private readonly ILogger<UrlValidator> _logger;

    private static readonly HashSet<string> BlockedHostnames = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "metadata.google.internal",
        "metadata",
        "instance-data"
    };

    // Private IP CIDR ranges (address bytes, prefix length)
    private static readonly (byte[] Network, int PrefixLength)[] PrivateIPv4Ranges =
    [
        (new byte[] { 10, 0, 0, 0 }, 8),       // 10.0.0.0/8
        ([172, 16, 0, 0], 12),     // 172.16.0.0/12
        ([192, 168, 0, 0], 16),    // 192.168.0.0/16
        ([127, 0, 0, 0], 8),       // 127.0.0.0/8
        ([169, 254, 0, 0], 16),    // 169.254.0.0/16
        ([0, 0, 0, 0], 8),         // 0.0.0.0/8
        ([100, 64, 0, 0], 10),     // 100.64.0.0/10 (CGN)
    ];

    private static readonly (byte[] Network, int PrefixLength)[] PrivateIPv6Ranges =
    [
        (IPAddress.IPv6Loopback.GetAddressBytes(), 128), // ::1/128
        ([0xfc, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 7),  // fc00::/7
        ([0xfe, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 10), // fe80::/10
    ];

    /// <summary>Initializes a new instance of <see cref="UrlValidator"/>.</summary>
    /// <param name="options">The URL security options.</param>
    /// <param name="logger">The logger.</param>
    public UrlValidator(IOptions<UrlSecurityOptions> options, ILogger<UrlValidator> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UrlValidationResult> ValidateUrlAsync(Uri url, CancellationToken ct = default)
    {
        if (url is null)
            return UrlValidationResult.Denied("URL is null or empty");

        if (!url.IsAbsoluteUri)
            return UrlValidationResult.Denied("Invalid URL format");

        var uri = url;
        var basicResult = ValidateBasicUrlProperties(uri, url.ToString());
        if (basicResult != null)
            return basicResult;

        var domainResult = ValidateDomainRules(uri.Host);
        if (domainResult != null)
            return domainResult;

        var ipResult = await ValidateIpAddressAsync(uri.Host, ct).ConfigureAwait(false);
        if (ipResult != null)
            return ipResult;

        return UrlValidationResult.Allowed(uri);
    }

    private UrlValidationResult? ValidateBasicUrlProperties(Uri uri, string url)
    {
        if (!_options.AllowedSchemes.Contains(uri.Scheme))
        {
            LogSsrfBlockedUrlWithDisallowed(uri.Scheme, url);
            return UrlValidationResult.Denied($"Scheme '{uri.Scheme}' is not allowed");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            LogSsrfBlockedUrlWithEmbedded(url);
            return UrlValidationResult.Denied("URLs with embedded credentials are not allowed");
        }

        if (BlockedHostnames.Contains(uri.Host))
        {
            LogSsrfBlockedRequestToKnown(uri.Host);
            return UrlValidationResult.Denied($"Hostname '{uri.Host}' is blocked");
        }

        if (uri.Port != -1 && _options.BlockedPorts.Contains(uri.Port))
        {
            LogSsrfBlockedRequestToRestricted(uri.Port, url);
            return UrlValidationResult.Denied($"Port {uri.Port} is blocked");
        }

        return null;
    }

    private UrlValidationResult? ValidateDomainRules(string host)
    {
        if (_options.AllowedDomains.Count > 0 && !IsDomainMatch(host, _options.AllowedDomains))
        {
            LogSsrfDomainNotInAllowlist(host);
            return UrlValidationResult.Denied($"Domain '{host}' is not in the allowed domains list");
        }

        if (_options.BlockedDomains.Count > 0 && IsDomainMatch(host, _options.BlockedDomains))
        {
            LogSsrfDomainIsInBlocklist(host);
            return UrlValidationResult.Denied($"Domain '{host}' is blocked");
        }

        return null;
    }

    private async Task<UrlValidationResult?> ValidateIpAddressAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var directIp))
        {
            if (_options.BlockPrivateIPs && IsPrivateIP(directIp))
            {
                LogSsrfBlockedRequestToPrivate(directIp);
                return UrlValidationResult.Denied($"IP address '{directIp}' is in a private/reserved range");
            }
            return null;
        }

        if (!_options.BlockPrivateIPs || !_options.ResolveDNS)
            return null;

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct).ConfigureAwait(false);
            var privateAddress = addresses.FirstOrDefault(IsPrivateIP);
            if (privateAddress != null)
            {
                LogSsrfDnsResolutionOfResolved(host, privateAddress);
                return UrlValidationResult.Denied($"Hostname '{host}' resolves to a private/reserved IP address");
            }
        }
        catch (SocketException ex)
        {
            LogSsrfDnsResolutionFailedFor(ex, host);
            return UrlValidationResult.Denied($"DNS resolution failed for hostname '{host}'");
        }

        return null;
    }

    private static bool IsDomainMatch(string host, IReadOnlyList<string> domains)
    {
        return domains.Any(domain =>
            string.Equals(host, domain, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPrivateIP(IPAddress address)
    {
        // Handle IPv4-mapped IPv6 addresses
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return PrivateIPv4Ranges.Any(r => IsInCidrRange(bytes, r.Network, r.PrefixLength));
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return PrivateIPv6Ranges.Any(r => IsInCidrRange(bytes, r.Network, r.PrefixLength));
        }

        return false;
    }

    private static bool IsInCidrRange(byte[] address, byte[] network, int prefixLength)
    {
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        // Compare full bytes
        for (int i = 0; i < fullBytes && i < address.Length && i < network.Length; i++)
        {
            if (address[i] != network[i])
                return false;
        }

        // Compare remaining bits
        if (remainingBits > 0 && fullBytes < address.Length && fullBytes < network.Length)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((address[fullBytes] & mask) != (network[fullBytes] & mask))
                return false;
        }

        return true;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "SSRF: Blocked URL with disallowed scheme {Scheme}: {Url}")]
    private partial void LogSsrfBlockedUrlWithDisallowed(object scheme, object url);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "SSRF: Blocked URL with embedded credentials: {Url}")]
    private partial void LogSsrfBlockedUrlWithEmbedded(object url);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "SSRF: Blocked request to known internal hostname: {Host}")]
    private partial void LogSsrfBlockedRequestToKnown(object host);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "SSRF: Blocked request to restricted port {Port}: {Url}")]
    private partial void LogSsrfBlockedRequestToRestricted(int port, object url);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "SSRF: Domain {Host} not in allowlist")]
    private partial void LogSsrfDomainNotInAllowlist(object host);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "SSRF: Domain {Host} is in blocklist")]
    private partial void LogSsrfDomainIsInBlocklist(object host);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "SSRF: Blocked request to private IP {IP}")]
    private partial void LogSsrfBlockedRequestToPrivate(object iP);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "SSRF: DNS resolution of {Host} resolved to private IP {IP}")]
    private partial void LogSsrfDnsResolutionOfResolved(object host, object iP);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "SSRF: DNS resolution failed for {Host}")]
    private partial void LogSsrfDnsResolutionFailedFor(Exception ex, object host);

}
