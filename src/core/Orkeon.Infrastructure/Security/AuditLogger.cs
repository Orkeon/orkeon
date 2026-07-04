using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
namespace Orkeon.Infrastructure.Security;

using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;
using Orkeon.Infrastructure.Configuration;

/// <summary>
/// Default implementation of <see cref="IAuditLogger"/> that dispatches events to registered sinks.
/// </summary>
public sealed partial class AuditLogger : IAuditLogger
{
    private readonly List<IAuditSink> _sinks;
    private readonly AuditOptions _options;
    private readonly ILogger<AuditLogger> _logger;

    /// <summary>Initializes a new instance of <see cref="AuditLogger"/>.</summary>
    /// <param name="sinks">The audit sinks to dispatch events to.</param>
    /// <param name="options">The audit configuration options.</param>
    /// <param name="logger">The logger.</param>
    public AuditLogger(
        IEnumerable<IAuditSink> sinks,
        IOptions<AuditOptions> options,
        ILogger<AuditLogger> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _sinks = sinks.ToList();
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task LogAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        return LogCoreAsync();

        async Task LogCoreAsync()
        {
            if (!ShouldLog(auditEvent))
                return;

            var tasks = _sinks.Select(sink => DispatchToSinkAsync(sink, auditEvent, ct));
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public IAuditScope BeginScope(string correlationId, string? crewId = null)
    {
        return new AuditScope(this, correlationId, crewId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery query, CancellationToken ct = default)
    {
        var queryableSink = _sinks.OfType<IQueryableAuditSink>().FirstOrDefault();
        if (queryableSink is null)
        {
            LogNoQueryableAuditSinkRegistered();
            return Array.Empty<AuditEvent>();
        }

        return await queryableSink.QueryAsync(query, ct).ConfigureAwait(false);
    }

    private bool ShouldLog(AuditEvent auditEvent)
    {
        if (!_options.Enabled)
            return false;

        if (auditEvent.Severity < _options.MinSeverity)
            return false;

        if (_options.EnabledCategories.Count > 0 && !_options.EnabledCategories.Contains(auditEvent.Category))
            return false;

        return true;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Per-sink fault barrier: a failure writing to one audit sink is logged and swallowed so one faulty sink cannot break audit dispatch to the others.")]
    private async Task DispatchToSinkAsync(IAuditSink sink, AuditEvent auditEvent, CancellationToken ct)
    {
        try
        {
            await sink.WriteAsync(auditEvent, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogAuditSinkFailedToWrite(ex, sink.GetType().Name, auditEvent.EventId);
        }
    }

    private sealed class AuditScope : IAuditScope
    {
        private readonly AuditLogger _logger;
        private readonly string? _crewId;

        /// <inheritdoc />
        public string CorrelationId { get; }

        public AuditScope(AuditLogger logger, string correlationId, string? crewId)
        {
            _logger = logger;
            _crewId = crewId;
            CorrelationId = correlationId;
        }

        /// <inheritdoc />
        public Task LogAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            // Enrich the event with scope context
            var enriched = auditEvent with
            {
                CorrelationId = CorrelationId,
                CrewId = auditEvent.CrewId ?? _crewId
            };

            return _logger.LogAsync(enriched, ct);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            // No resources to dispose
        }
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Warning, Message = "No queryable audit sink registered. Returning empty results.")]
    private partial void LogNoQueryableAuditSinkRegistered();

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Error, Message = "Audit sink {SinkType} failed to write event {EventId}")]
    private partial void LogAuditSinkFailedToWrite(Exception ex, object sinkType, object eventId);

}
