using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;

namespace Orkeon.Infrastructure.Tests.Doubles;

/// <summary>
/// Manual mock for IAuditLogger with call tracking.
/// </summary>
public class MockAuditLogger : IAuditLogger
{
    private IReadOnlyList<AuditEvent> _queryResult = Array.Empty<AuditEvent>();

    // --- Tracking ---
    public int LogCallCount { get; private set; }
    public AuditEvent? LastLoggedEvent { get; private set; }
    public List<AuditEvent> AllLoggedEvents { get; } = [];

    public int BeginScopeCallCount { get; private set; }
    public string? LastScopeCorrelationId { get; private set; }

    public int QueryCallCount { get; private set; }
    public AuditQuery? LastQuery { get; private set; }

    // --- Configuration ---
    public void SetQueryResult(IReadOnlyList<AuditEvent> result) => _queryResult = result;

    // --- IAuditLogger ---
    public Task LogAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        LogCallCount++;
        LastLoggedEvent = auditEvent;
        AllLoggedEvents.Add(auditEvent);
        return Task.CompletedTask;
    }

    public IAuditScope BeginScope(string correlationId, string? crewId = null)
    {
        BeginScopeCallCount++;
        LastScopeCorrelationId = correlationId;
        return new MockAuditScope(correlationId, this);
    }

    public Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        QueryCallCount++;
        LastQuery = query;
        return Task.FromResult(_queryResult);
    }

    private class MockAuditScope : IAuditScope
    {
        private readonly MockAuditLogger _logger;

        public MockAuditScope(string correlationId, MockAuditLogger logger)
        {
            CorrelationId = correlationId;
            _logger = logger;
        }

        public string CorrelationId { get; }

        public Task LogAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            return _logger.LogAsync(auditEvent, ct);
        }

        public void Dispose() { }
    }
}
