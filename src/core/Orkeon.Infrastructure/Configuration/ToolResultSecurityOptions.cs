using Orkeon.Domain.Security;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Configuration options for tool result sanitization.
/// </summary>
public class ToolResultSecurityOptions
{
    /// <summary>
    /// Maximum allowed length for tool results. Results exceeding this are truncated.
    /// </summary>
    public int MaxToolResultLength { get; set; } = 50_000;

    /// <summary>
    /// The sanitization policy to apply to tool results. Default is Strip.
    /// </summary>
    public SanitizationPolicy Policy { get; set; } = SanitizationPolicy.Strip;

    /// <summary>
    /// Set of tool names whose results bypass sanitization.
    /// </summary>
    public HashSet<string> TrustedTools { get; } = [];
}
