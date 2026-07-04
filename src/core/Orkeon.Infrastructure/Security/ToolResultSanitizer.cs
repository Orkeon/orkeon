using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Infrastructure.Security;

/// <summary>
/// Result of tool result sanitization.
/// </summary>
public record ToolResultSanitization
{
    /// <summary>Gets the sanitized result string.</summary>
    public string SanitizedResult { get; init; } = string.Empty;
    /// <summary>Gets the original length of the result before truncation.</summary>
    public int OriginalLength { get; init; }
    /// <summary>Gets a value indicating whether the result was truncated.</summary>
    public bool WasTruncated { get; init; }
    /// <summary>Gets the list of threats detected in the result.</summary>
    public IReadOnlyList<ThreatDetection> Threats { get; init; } = Array.Empty<ThreatDetection>();
}

/// <summary>
/// Sanitizes tool results to prevent injection via tool output.
/// </summary>
public partial class ToolResultSanitizer
{
    private readonly IPromptSanitizer _sanitizer;
    private readonly ToolResultSecurityOptions _options;
    private readonly ILogger<ToolResultSanitizer> _logger;

    /// <summary>Initializes a new instance of <see cref="ToolResultSanitizer"/>.</summary>
    /// <param name="sanitizer">The prompt sanitizer used for threat detection.</param>
    /// <param name="options">The tool result security options.</param>
    /// <param name="logger">The logger.</param>
    public ToolResultSanitizer(
        IPromptSanitizer sanitizer,
        IOptions<ToolResultSecurityOptions> options,
        ILogger<ToolResultSanitizer> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _sanitizer = sanitizer;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sanitizes a tool result by truncating, sanitizing, and wrapping it.
    /// </summary>
    /// <param name="toolName">Name of the tool that produced the result.</param>
    /// <param name="result">The raw tool result.</param>
    /// <param name="agentRole">The role of the agent consuming this result.</param>
    /// <returns>The sanitized tool result.</returns>
    public ToolResultSanitization SanitizeToolResult(string toolName, string result, string agentRole)
    {
        if (string.IsNullOrEmpty(result))
        {
            return new ToolResultSanitization
            {
                SanitizedResult = string.Empty,
                OriginalLength = 0,
                WasTruncated = false
            };
        }

        var originalLength = result.Length;

        // Trusted tools bypass sanitization
        if (_options.TrustedTools.Contains(toolName))
        {
            return new ToolResultSanitization
            {
                SanitizedResult = result,
                OriginalLength = originalLength,
                WasTruncated = false
            };
        }

        // Truncate if too long
        var wasTruncated = false;
        var processed = result;
        if (processed.Length > _options.MaxToolResultLength)
        {
            processed = processed[.._options.MaxToolResultLength] + "\n[TRUNCATED]";
            wasTruncated = true;
            LogToolResultFromTruncatedFrom(toolName, originalLength, _options.MaxToolResultLength);
        }

        // Sanitize the result
        var context = new SanitizationContext(
            Source: $"tool:{toolName}",
            AgentRole: agentRole,
            IsTrusted: false);

        var sanitizationResult = _sanitizer.Sanitize(processed, context);

        if (sanitizationResult.Threats.Count > 0)
        {
            LogDetectedThreatInToolResult(sanitizationResult.Threats.Count, toolName, agentRole);
        }

        // Wrap with context delimiters
        var finalResult = sanitizationResult.IsBlocked
            ? "[Tool result blocked due to security concerns]"
            : _sanitizer.WrapUserData(sanitizationResult.SanitizedText, $"Tool Result: {toolName}");

        return new ToolResultSanitization
        {
            SanitizedResult = finalResult,
            OriginalLength = originalLength,
            WasTruncated = wasTruncated,
            Threats = sanitizationResult.Threats
        };
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Tool result from '{Tool}' truncated from {Original} to {Max} characters")]
    private partial void LogToolResultFromTruncatedFrom(object tool, object original, int max);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Detected {Count} threat(s) in tool result from '{Tool}' for agent '{Agent}'")]
    private partial void LogDetectedThreatInToolResult(int count, object tool, object agent);

}
