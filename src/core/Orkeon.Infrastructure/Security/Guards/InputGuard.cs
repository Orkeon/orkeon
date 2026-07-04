using Microsoft.Extensions.Logging;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;

namespace Orkeon.Infrastructure.Security.Guards;

/// <summary>
/// Guardian that checks input content for prompt injection and other threats
/// by delegating to the IPromptSanitizer.
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
            Source: "GuardianPipeline",
            AgentRole: context.AgentId,
            IsTrusted: false);

        var result = _sanitizer.Sanitize(context.Content, sanitizationContext);

        if (result.IsBlocked)
        {
            var violations = result.Threats.Select(t => new GuardViolation(
                nameof(InputGuard),
                GuardPhase.Input,
                $"Threat detected: {t.Type} - pattern '{t.Pattern}' at position {t.Position}",
                MapSeverity(t.Severity),
                DateTime.UtcNow)).ToList();

            LogInputBlockedBySanitizerThreats(result.Threats.Count);
            return Task.FromResult(GuardResult.Block(
                $"Input blocked: {result.Threats.Count} threat(s) detected", violations));
        }

        if (result.Threats.Count > 0)
        {
            var violations = result.Threats.Select(t => new GuardViolation(
                nameof(InputGuard),
                GuardPhase.Input,
                $"Threat detected: {t.Type} - pattern '{t.Pattern}' at position {t.Position}",
                MapSeverity(t.Severity),
                DateTime.UtcNow)).ToList();

            LogInputWarningsThreatsDetectedBut(result.Threats.Count);
            return Task.FromResult(GuardResult.Warn(
                $"{result.Threats.Count} threat(s) detected in input", violations));
        }

        return Task.FromResult(GuardResult.Allow());
    }

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
