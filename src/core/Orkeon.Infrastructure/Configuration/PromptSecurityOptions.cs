using System.Collections.ObjectModel;
using Orkeon.Domain.Security;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Configuration options for prompt security and injection detection.
/// </summary>
public class PromptSecurityOptions
{
    /// <summary>
    /// The sanitization policy to apply. Default is Strip.
    /// </summary>
    public SanitizationPolicy Policy { get; set; } = SanitizationPolicy.Strip;

    /// <summary>
    /// Custom regex patterns to detect in addition to built-in patterns.
    /// </summary>
    public Collection<string> CustomPatterns { get; } = [];

    /// <summary>
    /// Whether to enable detection of data exfiltration attempts. Default is true.
    /// </summary>
    public bool EnableExfiltrationDetection { get; set; } = true;
}
