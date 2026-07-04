using System.Collections.ObjectModel;
using Orkeon.Infrastructure.Constants.Security;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Configuration options for URL security validation to prevent SSRF attacks.
/// </summary>
public class UrlSecurityOptions
{
    /// <summary>
    /// Allowed URL schemes. Defaults to http and https.
    /// </summary>
    public HashSet<string> AllowedSchemes { get; } = new(StringComparer.OrdinalIgnoreCase) { "http", "https" };

    /// <summary>
    /// Ports that are blocked from access (common service ports).
    /// </summary>
    public HashSet<int> BlockedPorts { get; } = [.. SecurityDefaults.BlockedPorts];

    /// <summary>
    /// If non-empty, only these domains (and their subdomains) are allowed.
    /// </summary>
    public Collection<string> AllowedDomains { get; } = [];

    /// <summary>
    /// Domains (and their subdomains) that are explicitly blocked.
    /// </summary>
    public Collection<string> BlockedDomains { get; } = [];

    /// <summary>
    /// Whether to block requests to private/internal IP ranges. Defaults to true.
    /// </summary>
    public bool BlockPrivateIPs { get; set; } = true;

    /// <summary>
    /// Whether to resolve DNS and check the resolved IP against private ranges. Defaults to true.
    /// </summary>
    public bool ResolveDNS { get; set; } = true;
}
