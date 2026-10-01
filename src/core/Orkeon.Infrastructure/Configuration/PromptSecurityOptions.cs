using System.Collections.ObjectModel;
using Orkeon.Domain.Security;

namespace Orkeon.Infrastructure.Configuration;

/// <summary>
/// Configuration options for prompt security and injection detection.
/// </summary>
public class PromptSecurityOptions
{
    /// <summary>
    /// The policy the Guardian's input phase applies to the composed user prompt. Default
    /// <see cref="SanitizationPolicy.Block"/>: a High or Critical pattern fails the task before
    /// any provider call, a lower one is logged and audited; the prompt is never rewritten.
    /// </summary>
    public SanitizationPolicy Policy { get; set; } = SanitizationPolicy.Block;

    /// <summary>
    /// Custom regex patterns to detect in addition to built-in patterns.
    /// </summary>
    public Collection<string> CustomPatterns { get; } = [];

    /// <summary>
    /// Whether to enable detection of data exfiltration attempts. Default is true.
    /// </summary>
    public bool EnableExfiltrationDetection { get; set; } = true;
}
