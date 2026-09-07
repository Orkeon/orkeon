using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Orkeon.Domain.Tools.Security;

namespace Orkeon.Tools.Abstractions.Security;

/// <summary>
/// Minimal, dependency-free <b>fail-closed</b> SSRF guard used by <c>HttpToolBase.ValidateUrlAsync</c>
/// when no full <see cref="IUrlValidator"/> is injected. It enforces a safe default rather than the
/// previous fail-open behavior (which allowed any URL with an http/https scheme).
///
/// <para>
/// This is intentionally a reduced subset of the full <c>UrlValidator</c> (which lives in the
/// Infrastructure layer and cannot be referenced from this abstractions package). It blocks:
/// non-http(s) schemes, embedded credentials, well-known metadata/loopback hostnames, and any host
/// that is — or whose DNS resolution yields — a private / loopback / link-local / reserved IP.
/// For richer policies (allow/block domain lists, configurable ports) inject the full
/// <see cref="IUrlValidator"/>.
/// </para>
/// </summary>
internal static class DefaultSsrfGuard
{
    private static readonly HashSet<string> BlockedHostnames = new(StringComparer.OrdinalIgnoreCase)
    {
        "localhost",
        "metadata.google.internal",
        "metadata",
        "instance-data",
    };

    // Private / reserved IPv4 CIDR ranges (network bytes, prefix length).
    private static readonly (byte[] Network, int PrefixLength)[] PrivateIPv4Ranges =
    [
        ([10, 0, 0, 0], 8),       // 10.0.0.0/8
        ([172, 16, 0, 0], 12),    // 172.16.0.0/12
        ([192, 168, 0, 0], 16),   // 192.168.0.0/16
        ([127, 0, 0, 0], 8),      // 127.0.0.0/8 loopback
        ([169, 254, 0, 0], 16),   // 169.254.0.0/16 link-local (cloud metadata)
        ([0, 0, 0, 0], 8),        // 0.0.0.0/8
        ([100, 64, 0, 0], 10),    // 100.64.0.0/10 carrier-grade NAT
    ];

    // This table is duplicated verbatim in Orkeon.Infrastructure.Security.UrlValidator (which this
    // package cannot reference); SsrfGuardParityTests keeps the two copies honest.
    private static readonly (byte[] Network, int PrefixLength)[] PrivateIPv6Ranges =
    [
        (IPAddress.IPv6Loopback.GetAddressBytes(), 128),                            // ::1/128
        (IPAddress.IPv6Any.GetAddressBytes(), 128),                                 // ::/128 unspecified, which connect() turns into loopback
        ([0xfc, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 7),                  // fc00::/7 unique-local
        ([0xfe, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 10),              // fe80::/10 link-local
        ([0xff, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 8),                  // ff00::/8 multicast
        ([0, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 96),           // 64:ff9b::/96 NAT64 well-known prefix
    ];

    /// <summary>
    /// Validates <paramref name="url"/> with the fail-closed default policy.
    /// </summary>
    public static async Task<UrlValidationResult> ValidateAsync(Uri url, ILogger logger, CancellationToken ct)
    {
        if (url is null)
            return UrlValidationResult.Denied("URL is null or empty");

        if (!url.IsAbsoluteUri)
            return UrlValidationResult.Denied("Invalid URL format");

        var uri = url;

        if (uri.Scheme != "http" && uri.Scheme != "https")
            return UrlValidationResult.Denied($"Scheme '{uri.Scheme}' is not allowed");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            return UrlValidationResult.Denied("URLs with embedded credentials are not allowed");

        if (BlockedHostnames.Contains(uri.Host))
        {
            LogBlocked(logger, uri.Host, "known internal hostname");
            return UrlValidationResult.Denied($"Hostname '{uri.Host}' is blocked");
        }

        // Direct IP literal in the host.
        if (IPAddress.TryParse(uri.Host, out var directIp))
        {
            if (IsPrivateIP(directIp))
            {
                LogBlocked(logger, uri.Host, "private/reserved IP");
                return UrlValidationResult.Denied($"IP address '{directIp}' is in a private/reserved range");
            }
            return UrlValidationResult.Allowed(uri);
        }

        // Resolve the hostname and reject if any address is private (anti DNS-rebinding baseline).
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, ct).ConfigureAwait(false);
            if (addresses.Any(IsPrivateIP))
            {
                LogBlocked(logger, uri.Host, "resolves to a private/reserved IP");
                return UrlValidationResult.Denied($"Hostname '{uri.Host}' resolves to a private/reserved IP address");
            }
        }
        catch (SocketException)
        {
            return UrlValidationResult.Denied($"DNS resolution failed for hostname '{uri.Host}'");
        }

        return UrlValidationResult.Allowed(uri);
    }

    private static bool IsPrivateIP(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return PrivateIPv4Ranges.Any(r => IsInCidrRange(bytes, r.Network, r.PrefixLength));
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();

            // The deprecated IPv4-compatible form ::a.b.c.d carries its real destination in its
            // last four bytes, exactly like the ::ffff: mapped form handled above, so it has to
            // be judged by the IPv4 table: ::7f00:1 reaches loopback while matching no IPv6 range.
            if (IsIPv4Compatible(bytes) &&
                PrivateIPv4Ranges.Any(r => IsInCidrRange(bytes[12..], r.Network, r.PrefixLength)))
            {
                return true;
            }

            return PrivateIPv6Ranges.Any(r => IsInCidrRange(bytes, r.Network, r.PrefixLength));
        }

        return false;
    }

    private static bool IsIPv4Compatible(byte[] ipv6Bytes)
    {
        // ::a.b.c.d is simply an IPv6 address whose first 96 bits are zero. IPAddress offers no
        // predicate for it the way it does for the mapped form, hence the explicit prefix check.
        for (var i = 0; i < 12; i++)
        {
            if (ipv6Bytes[i] != 0)
                return false;
        }

        return true;
    }

    private static bool IsInCidrRange(byte[] address, byte[] network, int prefixLength)
    {
        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes && i < address.Length && i < network.Length; i++)
        {
            if (address[i] != network[i])
                return false;
        }

        if (remainingBits > 0 && fullBytes < address.Length && fullBytes < network.Length)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((address[fullBytes] & mask) != (network[fullBytes] & mask))
                return false;
        }

        return true;
    }

    private static void LogBlocked(ILogger logger, string host, string reason)
    {
        if (logger.IsEnabled(LogLevel.Warning))
        {
#pragma warning disable CA1848 // High-perf logging not required on a security cold path.
            logger.LogWarning("SSRF (default guard): blocked request to {Host} ({Reason})", host, reason);
#pragma warning restore CA1848
        }
    }
}
