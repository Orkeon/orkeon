using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Security;
using Orkeon.Infrastructure.Security.Sinks;

namespace Orkeon.Infrastructure.Tests.Security;

public class AuditLoggerTestsFixture
{
    private readonly List<IAuditSink> _sinks = [];
    private AuditOptions _options = new();
    private AuditLogger? _logger;

    // --- Fluent configuration ---

    public AuditLoggerTestsFixture WithSink(IAuditSink sink)
    {
        _sinks.Add(sink);
        return this;
    }

    public AuditLoggerTestsFixture WithSinks(params IAuditSink[] sinks)
    {
        _sinks.AddRange(sinks);
        return this;
    }

    public AuditLoggerTestsFixture WithOptions(AuditOptions options)
    {
        _options = options;
        return this;
    }

    public AuditLoggerTestsFixture WithMinSeverity(AuditSeverity severity)
    {
        _options.MinSeverity = severity;
        return this;
    }

    public AuditLoggerTestsFixture WithAuditDisabled()
    {
        _options.Enabled = false;
        return this;
    }

    public AuditLoggerTestsFixture WithEnabledCategories(params AuditCategory[] categories)
    {
        _options.EnabledCategories.Clear();
        foreach (var category in categories)
            _options.EnabledCategories.Add(category);
        return this;
    }

    // --- Build / Execution ---

    public AuditLogger Build()
    {
        _logger = new AuditLogger(
            _sinks,
            Options.Create(_options),
            NullLogger<AuditLogger>.Instance);
        return _logger;
    }

    public async Task LogAsync(AuditEvent evt)
    {
        var logger = _logger ?? Build();
        await logger.LogAsync(evt);
    }

    public IAuditScope BeginScope(string correlationId, string crewId)
    {
        var logger = _logger ?? Build();
        return logger.BeginScope(correlationId, crewId);
    }

    public async Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditQuery? query = null)
    {
        var logger = _logger ?? Build();
        return await logger.QueryAsync(query ?? new AuditQuery());
    }

    // --- Factories ---

    public static AuditEvent CreateTestEvent(
        AuditSeverity severity = AuditSeverity.Info,
        AuditCategory category = AuditCategory.LlmCall) => new()
        {
            EventId = Guid.NewGuid().ToString("N"),
            Timestamp = DateTime.UtcNow,
            Category = category,
            Action = "Test action",
            Outcome = AuditOutcome.Success,
            CorrelationId = "test-correlation",
            Severity = severity
        };

    public static InMemoryAuditSink CreateInMemorySink() => new();

    public static MockAuditSink CreateMockSink() => new();
}
