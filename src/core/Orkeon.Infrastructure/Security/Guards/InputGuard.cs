using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;

namespace Orkeon.Infrastructure.Security.Guards;

/// <summary>
/// Guardian of the <see cref="GuardPhase.Input"/> phase: screens the composed user prompt of an
/// agent turn (task, previous outputs, retrieved knowledge) with the <see cref="IPromptSanitizer"/>
/// under <c>Security:Prompt:Policy</c>. Default <c>Block</c>: a High or Critical pattern blocks
/// the turn, a lower one is a warning. The prompt is never rewritten.
/// </summary>
public partial class InputGuard : IGuardian
{
    private readonly IPromptSanitizer _sanitizer;
    private readonly ILogger<InputGuard> _logger;

    /// <summary>Initializes a new instance of <see cref="InputGuard"/>.</summary>
    /// <param name="sanitizer">The prompt sanitizer used to detect threats in input content.</param>
    /// <param name="logger">The logger.</param>
    public InputGuard(IPromptSanitizer sanitizer, ILogger<InputGuard> logger)
    {
        ArgumentNullException.ThrowIfNull(sanitizer);
        _sanitizer = sanitizer;
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<GuardResult> CheckAsync(GuardContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Phase != GuardPhase.Input || context.Content is null)
            return Task.FromResult(GuardResult.Allow());

        var sanitizationContext = new SanitizationContext(
            Source: "agent-input",
            AgentRole: string.IsNullOrEmpty(context.AgentRole) ? context.AgentId : context.AgentRole,
            IsTrusted: false);

        var result = _sanitizer.Sanitize(context.Content, sanitizationContext);

        if (result.IsBlocked)
        {
            var violations = result.Threats.Select(t => new GuardViolation(
                nameof(InputGuard),
                GuardPhase.Input,
                $"Threat detected: {t.Type} - {t.Pattern} at position {t.Position}",
                MapSeverity(t.Severity),
                DateTime.UtcNow)).ToList();

            LogInputBlockedBySanitizerThreats(result.Threats.Count);
            return Task.FromResult(GuardResult.Block(
                $"prompt injection suspected — {result.Threats.Count} pattern(s) detected ({Describe(result.Threats)}); " +
                "set Security:Prompt:Policy to Warn to let such input through with a warning", violations));
        }

        if (result.Threats.Count > 0)
        {
            var violations = result.Threats.Select(t => new GuardViolation(
                nameof(InputGuard),
                GuardPhase.Input,
                $"Threat detected: {t.Type} - {t.Pattern} at position {t.Position}",
                MapSeverity(t.Severity),
                DateTime.UtcNow)).ToList();

            LogInputWarningsThreatsDetectedBut(result.Threats.Count);
            return Task.FromResult(GuardResult.Warn(
                $"{result.Threats.Count} pattern(s) detected in input ({Describe(result.Threats)})", violations));
        }

        return Task.FromResult(GuardResult.Allow());
    }

    private static string Describe(IReadOnlyList<ThreatDetection> threats)
        => string.Join(", ", threats.Select(t => t.Pattern).Distinct(StringComparer.Ordinal));

    private static GuardThreatSeverity MapSeverity(ThreatSeverity severity) => severity switch
    {
        ThreatSeverity.Low => GuardThreatSeverity.Low,
        ThreatSeverity.Medium => GuardThreatSeverity.Medium,
        ThreatSeverity.High => GuardThreatSeverity.High,
        ThreatSeverity.Critical => GuardThreatSeverity.Critical,
        _ => GuardThreatSeverity.Low
    };

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "Input blocked by sanitizer: {ThreatCount} threats detected")]
    private partial void LogInputBlockedBySanitizerThreats(int threatCount);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Information, Message = "Input warnings: {ThreatCount} threats detected but not blocked")]
    private partial void LogInputWarningsThreatsDetectedBut(int threatCount);

}
