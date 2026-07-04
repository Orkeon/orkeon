using System.Globalization;
using Microsoft.Extensions.Logging;
namespace Orkeon.Infrastructure.Security.Sinks;

using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;

/// <summary>
/// Audit sink that writes events via structured logging (ILogger).
/// </summary>
public sealed class StructuredLogAuditSink : IAuditSink
{
    private const string AuditMessageFormat =
        "AUDIT [{Category}] {Action} -> {Outcome} | CorrelationId={CorrelationId} Agent={AgentRole} Duration={DurationMs}ms";

    // Cached LoggerMessage.Define delegates: the audit level is dynamic (derived from
    // AuditEvent.Severity), so a [LoggerMessage] source-gen method (which bakes in a fixed
    // Level) cannot be used. One delegate is cached per supported level instead.
    private static readonly Action<ILogger, AuditCategory, string, AuditOutcome, string, string, string, Exception?> s_logAuditDebug =
        LoggerMessage.Define<AuditCategory, string, AuditOutcome, string, string, string>(LogLevel.Debug, new EventId(1, nameof(StructuredLogAuditSink)), AuditMessageFormat);

    private static readonly Action<ILogger, AuditCategory, string, AuditOutcome, string, string, string, Exception?> s_logAuditInformation =
        LoggerMessage.Define<AuditCategory, string, AuditOutcome, string, string, string>(LogLevel.Information, new EventId(2, nameof(StructuredLogAuditSink)), AuditMessageFormat);

    private static readonly Action<ILogger, AuditCategory, string, AuditOutcome, string, string, string, Exception?> s_logAuditWarning =
        LoggerMessage.Define<AuditCategory, string, AuditOutcome, string, string, string>(LogLevel.Warning, new EventId(3, nameof(StructuredLogAuditSink)), AuditMessageFormat);

    private static readonly Action<ILogger, AuditCategory, string, AuditOutcome, string, string, string, Exception?> s_logAuditError =
        LoggerMessage.Define<AuditCategory, string, AuditOutcome, string, string, string>(LogLevel.Error, new EventId(4, nameof(StructuredLogAuditSink)), AuditMessageFormat);

    private static readonly Action<ILogger, AuditCategory, string, AuditOutcome, string, string, string, Exception?> s_logAuditCritical =
        LoggerMessage.Define<AuditCategory, string, AuditOutcome, string, string, string>(LogLevel.Critical, new EventId(5, nameof(StructuredLogAuditSink)), AuditMessageFormat);

    private readonly ILogger<StructuredLogAuditSink> _logger;

    /// <summary>Initializes a new instance of <see cref="StructuredLogAuditSink"/>.</summary>
    /// <param name="logger">The logger to write audit events to.</param>
    public StructuredLogAuditSink(ILogger<StructuredLogAuditSink> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        var logLevel = MapSeverityToLogLevel(auditEvent.Severity);

        if (_logger.IsEnabled(logLevel))
        {
            var logAudit = SelectAuditLogger(logLevel);
            logAudit(
                _logger,
                auditEvent.Category,
                auditEvent.Action,
                auditEvent.Outcome,
                auditEvent.CorrelationId,
                auditEvent.AgentRole ?? "N/A",
                auditEvent.DurationMs?.ToString(CultureInfo.InvariantCulture) ?? "N/A",
                null);
        }

        return Task.CompletedTask;
    }

    private static Action<ILogger, AuditCategory, string, AuditOutcome, string, string, string, Exception?> SelectAuditLogger(LogLevel logLevel)
        => logLevel switch
        {
            LogLevel.Debug => s_logAuditDebug,
            LogLevel.Warning => s_logAuditWarning,
            LogLevel.Error => s_logAuditError,
            LogLevel.Critical => s_logAuditCritical,
            _ => s_logAuditInformation
        };

    private static LogLevel MapSeverityToLogLevel(AuditSeverity severity) => severity switch
    {
        AuditSeverity.Debug => LogLevel.Debug,
        AuditSeverity.Info => LogLevel.Information,
        AuditSeverity.Warning => LogLevel.Warning,
        AuditSeverity.Error => LogLevel.Error,
        AuditSeverity.Critical => LogLevel.Critical,
        _ => LogLevel.Information
    };
}
