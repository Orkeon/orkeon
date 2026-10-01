using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;
using Orkeon.Infrastructure.Configuration;

namespace Orkeon.Infrastructure.Security;

/// <summary>
/// Screens a tool result before it enters the conversation — the first vector of indirect
/// prompt injection (a web page, a document, an API response that says "ignore previous
/// instructions"). Under the default <see cref="SanitizationPolicy.Warn"/> the result reaches
/// the model tagged as data, not instructions, and any pattern found is reported; under
/// <see cref="SanitizationPolicy.Block"/> a High or Critical pattern withholds the result and
/// the model is told so. The result itself is never rewritten. Its length is not this class's
/// concern: the tool-invocation pipeline has already applied the one truncation rule.
/// </summary>
public sealed partial class ToolResultSanitizer : IToolResultSanitizer
{
    private readonly IPromptSanitizer _detector;
    private readonly ToolResultSecurityOptions _options;
    private readonly ILogger<ToolResultSanitizer> _logger;

    /// <summary>Initializes a new instance of <see cref="ToolResultSanitizer"/>.</summary>
    /// <param name="detector">The prompt sanitizer whose patterns detect injections.</param>
    /// <param name="options">The tool result security options.</param>
    /// <param name="logger">The logger.</param>
    public ToolResultSanitizer(
        IPromptSanitizer detector,
        IOptions<ToolResultSecurityOptions> options,
        ILogger<ToolResultSanitizer> logger)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _detector = detector;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public ToolResultSanitization Sanitize(string toolName, string result, string agentRole)
    {
        ArgumentNullException.ThrowIfNull(toolName);
        if (string.IsNullOrEmpty(result)
            || _options.Policy == SanitizationPolicy.None
            || _options.TrustedTools.Contains(toolName))
        {
            return new ToolResultSanitization { Text = result ?? string.Empty };
        }

        var threats = _detector.Detect(result);
        if (threats.Count > 0)
            LogThreatsDetected(threats.Count, toolName, agentRole);

        if (_options.Policy == SanitizationPolicy.Block
            && threats.Any(t => t.Severity >= ThreatSeverity.High))
        {
            return new ToolResultSanitization
            {
                Text = $"[Tool result of '{toolName}' withheld by Security:ToolResults:Policy=Block: " +
                       $"{threats.Count} prompt-injection pattern(s) detected ({DescribeThreats(threats)}).]",
                Blocked = true,
                Threats = threats,
            };
        }

        return new ToolResultSanitization
        {
            Text = _detector.WrapUserData(result, $"Tool Result: {toolName}"),
            Threats = threats,
        };
    }

    private static string DescribeThreats(IReadOnlyList<ThreatDetection> threats)
        => string.Join(", ", threats.Select(t => t.Pattern).Distinct(StringComparer.Ordinal));

    [LoggerMessage(Level = LogLevel.Debug, Message = "Detected {Count} prompt-injection pattern(s) in the result of '{Tool}' for agent '{Agent}'")]
    private partial void LogThreatsDetected(int count, string tool, string agent);
}
