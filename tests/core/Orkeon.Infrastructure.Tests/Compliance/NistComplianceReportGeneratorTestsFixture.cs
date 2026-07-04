using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;
using Orkeon.Infrastructure.Compliance;
using Microsoft.Extensions.Logging.Abstractions;

namespace Orkeon.Infrastructure.Tests.Compliance;

/// <summary>
/// Test fixture for NistComplianceReportGenerator tests.
/// Provides a configurable fake IAuditLogger and factory for the SUT.
/// </summary>
public class NistComplianceReportGeneratorTestsFixture
{
    private readonly FakeAuditLogger _auditLogger = new();

    /// <summary>
    /// Adds audit events for the specified categories so they appear as covered.
    /// </summary>
    public NistComplianceReportGeneratorTestsFixture WithAuditEventsForCategories(
        params AuditCategory[] categories)
    {
        foreach (var category in categories)
        {
            _auditLogger.AddEvent(CreateEvent(category));
        }
        return this;
    }

    /// <summary>
    /// Adds audit events for all AuditCategory values.
    /// </summary>
    public NistComplianceReportGeneratorTestsFixture WithAllCategoriesCovered()
    {
        foreach (var category in Enum.GetValues<AuditCategory>())
        {
            _auditLogger.AddEvent(CreateEvent(category));
        }
        return this;
    }

    /// <summary>
    /// Creates the system under test.
    /// </summary>
    public NistComplianceReportGenerator CreateGenerator()
        => new(_auditLogger, NullLogger<NistComplianceReportGenerator>.Instance);

    public FakeAuditLogger GetAuditLogger() => _auditLogger;

    private static AuditEvent CreateEvent(AuditCategory category) => new()
    {
        EventId = Guid.NewGuid().ToString("N"),
        Timestamp = DateTime.UtcNow,
        Category = category,
        Action = $"test_{category}",
        Outcome = AuditOutcome.Success,
        CorrelationId = Guid.NewGuid().ToString("N"),
    };

    /// <summary>
    /// A simple in-memory IAuditLogger fake for testing.
    /// </summary>
    public sealed class FakeAuditLogger : IAuditLogger
    {
        private readonly List<AuditEvent> _events = [];

        public void AddEvent(AuditEvent auditEvent) => _events.Add(auditEvent);

        public Task LogAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            _events.Add(auditEvent);
            return Task.CompletedTask;
        }

        public IAuditScope BeginScope(string correlationId, string? crewId = null)
            => new FakeAuditScope(correlationId);

        public Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default)
        {
            IEnumerable<AuditEvent> result = _events;

            if (query.Category.HasValue)
                result = result.Where(e => e.Category == query.Category.Value);

            if (query.CorrelationId != null)
                result = result.Where(e => e.CorrelationId == query.CorrelationId);

            if (query.AgentRole != null)
                result = result.Where(e => e.AgentRole == query.AgentRole);

            if (query.From.HasValue)
                result = result.Where(e => e.Timestamp >= query.From.Value);

            if (query.To.HasValue)
                result = result.Where(e => e.Timestamp <= query.To.Value);

            if (query.MinSeverity.HasValue)
                result = result.Where(e => e.Severity >= query.MinSeverity.Value);

            var list = result.Take(query.Limit).ToList();
            return Task.FromResult<IReadOnlyList<AuditEvent>>(list.AsReadOnly());
        }

        private sealed class FakeAuditScope : IAuditScope
        {
            public string CorrelationId { get; }

            public FakeAuditScope(string correlationId)
                => CorrelationId = correlationId;

            public Task LogAsync(AuditEvent auditEvent, CancellationToken ct = default)
                => Task.CompletedTask;

            public void Dispose() { }
        }
    }
}
