using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Domain.Security;
using Orkeon.Infrastructure.Security.Sinks;

namespace Orkeon.Infrastructure.Tests.CovSecurity;

/// <summary>
/// Coverage for <see cref="StructuredLogAuditSink"/> including every severity
/// mapping and the disabled-log-level short circuit.
/// </summary>
public sealed class CovSecurity_StructuredLogAuditSinkTests
{
    private static AuditEvent Event(AuditSeverity severity, string? agentRole = "researcher", long? durationMs = 42) => new()
    {
        EventId = "evt-1",
        Timestamp = DateTime.UtcNow,
        Category = AuditCategory.SecurityEvent,
        Action = "test-action",
        Outcome = AuditOutcome.Success,
        CorrelationId = "corr-1",
        AgentRole = agentRole,
        DurationMs = durationMs,
        Severity = severity
    };

    /// <summary>Capturing logger that records the level of each emitted entry.</summary>
    private sealed class CapturingLogger : ILogger
    {
        public List<LogLevel> Levels { get; } = [];
        private readonly LogLevel _minLevel;
        public CapturingLogger(LogLevel minLevel = LogLevel.Trace) => _minLevel = minLevel;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= _minLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    private sealed class TypedLogger(CapturingLogger inner) : ILogger<StructuredLogAuditSink>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);
        public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => inner.Log(logLevel, eventId, state, exception, formatter);
    }

    [Theory]
    [InlineData(AuditSeverity.Debug, LogLevel.Debug)]
    [InlineData(AuditSeverity.Info, LogLevel.Information)]
    [InlineData(AuditSeverity.Warning, LogLevel.Warning)]
    [InlineData(AuditSeverity.Error, LogLevel.Error)]
    [InlineData(AuditSeverity.Critical, LogLevel.Critical)]
    public async Task WriteAsync_ShouldMapSeverityToLogLevel(AuditSeverity severity, LogLevel expected)
    {
        var capturing = new CapturingLogger();
        var sink = new StructuredLogAuditSink(new TypedLogger(capturing));

        await sink.WriteAsync(Event(severity), TestContext.Current.CancellationToken);

        Assert.Single(capturing.Levels);
        Assert.Equal(expected, capturing.Levels[0]);
    }

    [Fact]
    public async Task WriteAsync_ShouldNotLog_WhenLevelDisabled()
    {
        // Only Critical enabled; an Info event must be skipped.
        var capturing = new CapturingLogger(LogLevel.Critical);
        var sink = new StructuredLogAuditSink(new TypedLogger(capturing));

        await sink.WriteAsync(Event(AuditSeverity.Info), TestContext.Current.CancellationToken);

        Assert.Empty(capturing.Levels);
    }

    [Fact]
    public async Task WriteAsync_ShouldHandleNullAgentRoleAndDuration()
    {
        var capturing = new CapturingLogger();
        var sink = new StructuredLogAuditSink(new TypedLogger(capturing));

        await sink.WriteAsync(Event(AuditSeverity.Info, agentRole: null, durationMs: null), TestContext.Current.CancellationToken);

        Assert.Single(capturing.Levels);
    }

    [Fact]
    public async Task WriteAsync_ShouldReturnCompletedTask_WithNullLogger()
    {
        var sink = new StructuredLogAuditSink(NullLogger<StructuredLogAuditSink>.Instance);

        var ex = await Record.ExceptionAsync(() => sink.WriteAsync(Event(AuditSeverity.Info), TestContext.Current.CancellationToken));

        Assert.Null(ex);
    }
}
