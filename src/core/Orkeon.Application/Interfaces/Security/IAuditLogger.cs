
namespace Orkeon.Application.Interfaces.Security;

using Orkeon.Domain.Security;

/// <summary>
/// Primary interface for logging audit events.
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Logs a single audit event to all configured sinks.
    /// </summary>
    System.Threading.Tasks.Task LogAsync(AuditEvent auditEvent, CancellationToken ct = default);

    /// <summary>
    /// Creates a scoped audit context with a shared correlation ID.
    /// </summary>
    IAuditScope BeginScope(string correlationId, string? crewId = null);

    /// <summary>
    /// Queries audit events from the first available queryable sink.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default);
}

/// <summary>
/// A scoped audit context that automatically sets the correlation ID on logged events.
/// </summary>
public interface IAuditScope : IDisposable
{
    /// <summary>
    /// The correlation ID for this scope.
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// Logs an audit event within this scope.
    /// </summary>
    System.Threading.Tasks.Task LogAsync(AuditEvent auditEvent, CancellationToken ct = default);
}

/// <summary>
/// A sink that receives and persists audit events.
/// </summary>
public interface IAuditSink
{
    /// <summary>
    /// Writes a single audit event to the sink.
    /// </summary>
    System.Threading.Tasks.Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default);
}

/// <summary>
/// An audit sink that also supports querying stored events.
/// </summary>
public interface IQueryableAuditSink : IAuditSink
{
    /// <summary>
    /// Queries audit events matching the specified criteria.
    /// </summary>
    System.Threading.Tasks.Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default);
}

/// <summary>
/// Query parameters for searching audit events.
/// </summary>
public record AuditQuery(
    AuditCategory? Category = null,
    string? CorrelationId = null,
    string? AgentRole = null,
    DateTime? From = null,
    DateTime? To = null,
    AuditSeverity? MinSeverity = null,
    int Limit = 100);
