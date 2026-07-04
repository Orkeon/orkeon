using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IAuditSink with call tracking.
/// </summary>
public class MockAuditSink : IAuditSink
{
    // --- Tracking ---
    public int WriteCallCount { get; private set; }
    public AuditEvent? LastWrittenEvent { get; private set; }
    public List<AuditEvent> AllWrittenEvents { get; } = [];

    private Exception? _writeException;

    // --- Configuration ---

    /// <summary>
    /// When set, WriteAsync will throw this exception.
    /// </summary>
    public void SetWriteException(Exception exception) => _writeException = exception;

    // --- IAuditSink ---
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        WriteCallCount++;
        LastWrittenEvent = auditEvent;
        AllWrittenEvents.Add(auditEvent);

        if (_writeException != null)
            throw _writeException;

        return Task.CompletedTask;
    }
}

/// <summary>
/// Manual mock for IQueryableAuditSink with call tracking and configurable results.
/// </summary>
public class MockQueryableAuditSink : IQueryableAuditSink
{
    private IReadOnlyList<AuditEvent> _queryResult = Array.Empty<AuditEvent>();

    // --- Tracking ---
    public int WriteCallCount { get; private set; }
    public AuditEvent? LastWrittenEvent { get; private set; }
    public List<AuditEvent> AllWrittenEvents { get; } = [];

    public int QueryCallCount { get; private set; }
    public AuditQuery? LastQuery { get; private set; }

    // --- Configuration ---
    public void SetQueryResult(IReadOnlyList<AuditEvent> result) => _queryResult = result;

    // --- IAuditSink ---
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        WriteCallCount++;
        LastWrittenEvent = auditEvent;
        AllWrittenEvents.Add(auditEvent);
        return Task.CompletedTask;
    }

    // --- IQueryableAuditSink ---
    public Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        QueryCallCount++;
        LastQuery = query;
        return Task.FromResult(_queryResult);
    }
}
