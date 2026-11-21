using System.Collections.Concurrent;
using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;

namespace Orkeon.Hosting.Tests.Doubles;

/// <summary>An audit that keeps every event it is given, in order.</summary>
public sealed class FakeAuditLogger : IAuditLogger
{
    /// <summary>The events logged.</summary>
    public ConcurrentQueue<AuditEvent> Events { get; } = new();

    /// <inheritdoc />
    public Task LogAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        Events.Enqueue(auditEvent);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IAuditScope BeginScope(string correlationId, string? crewId = null) => new Scope(correlationId, this);

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AuditEvent>>([.. Events]);

    private sealed class Scope(string correlationId, FakeAuditLogger owner) : IAuditScope
    {
        public string CorrelationId { get; } = correlationId;

        public Task LogAsync(AuditEvent auditEvent, CancellationToken ct = default) => owner.LogAsync(auditEvent, ct);

        public void Dispose()
        {
        }
    }
}
