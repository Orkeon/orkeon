namespace Orkeon.Infrastructure.Security.Sinks;

using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;

/// <summary>
/// In-memory audit sink for testing and development scenarios.
/// </summary>
public sealed class InMemoryAuditSink : IQueryableAuditSink
{
    private readonly ConcurrentBag<AuditEvent> _events = [];

    /// <summary>
    /// Gets all stored events (for test assertions).
    /// </summary>
    public IReadOnlyCollection<AuditEvent> Events => _events.ToArray();

    /// <inheritdoc />
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        _events.Add(auditEvent);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var results = _events
            .Where(evt => MatchesQuery(evt, query))
            .OrderByDescending(evt => evt.Timestamp)
            .Take(query.Limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<AuditEvent>>(results);
    }

    private static bool MatchesQuery(AuditEvent evt, AuditQuery query)
    {
        if (query.Category.HasValue && evt.Category != query.Category.Value)
            return false;

        if (query.CorrelationId is not null && evt.CorrelationId != query.CorrelationId)
            return false;

        if (query.AgentRole is not null && evt.AgentRole != query.AgentRole)
            return false;

        if (query.From.HasValue && evt.Timestamp < query.From.Value)
            return false;

        if (query.To.HasValue && evt.Timestamp > query.To.Value)
            return false;

        if (query.MinSeverity.HasValue && evt.Severity < query.MinSeverity.Value)
            return false;

        return true;
    }

    /// <summary>
    /// Clears all stored events.
    /// </summary>
    public void Clear() => _events.Clear();
}
