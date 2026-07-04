using Orkeon.Application.Interfaces.Security;
using Orkeon.Domain.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
namespace Orkeon.Infrastructure.Tests.Security;

using Orkeon.Infrastructure.Configuration;
using Orkeon.Infrastructure.Tests.Doubles;
using Orkeon.Infrastructure.Security.Sinks;
using AuditLoggerSut = Orkeon.Infrastructure.Security.AuditLogger;

public class AuditLoggerTests
{
    private static AuditEvent CreateTestEvent(
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

    private static AuditLoggerSut CreateLogger(
        IEnumerable<IAuditSink>? sinks = null,
        AuditOptions? options = null)
    {
        return new AuditLoggerSut(
            sinks ?? Array.Empty<IAuditSink>(),
            Options.Create(options ?? new AuditOptions()),
            NullLogger<AuditLoggerSut>.Instance);
    }

    [Fact]
    public async Task ShouldDispatchToAllSinks_WhenLoggingEvent()
    {
        // Arrange
        var sink1 = new InMemoryAuditSink();
        var sink2 = new InMemoryAuditSink();
        var logger = CreateLogger([sink1, sink2]);
        var evt = CreateTestEvent();

        // Act
        await logger.LogAsync(evt, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(sink1.Events);
        Assert.Single(sink2.Events);
    }

    [Fact]
    public async Task ShouldNotPreventOtherSinks_WhenOneSinkFails()
    {
        // Arrange
        var failingSink = new MockAuditSink();
        failingSink.SetWriteException(new InvalidOperationException("Sink failure"));

        var healthySink = new InMemoryAuditSink();
        var logger = CreateLogger([failingSink, healthySink]);
        var evt = CreateTestEvent();

        // Act
        await logger.LogAsync(evt, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(healthySink.Events);
    }

    [Fact]
    public async Task ShouldFilterDebugEvents_WhenMinSeverityIsInfo()
    {
        // Arrange
        var sink = new InMemoryAuditSink();
        var options = new AuditOptions { MinSeverity = AuditSeverity.Info };
        var logger = CreateLogger([sink], options);

        var debugEvent = CreateTestEvent(severity: AuditSeverity.Debug);
        var infoEvent = CreateTestEvent(severity: AuditSeverity.Info);

        // Act
        await logger.LogAsync(debugEvent, TestContext.Current.CancellationToken);
        await logger.LogAsync(infoEvent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(sink.Events);
        Assert.Equal(AuditSeverity.Info, sink.Events.First().Severity);
    }

    [Fact]
    public async Task ShouldSkipAllLogging_WhenAuditIsDisabled()
    {
        // Arrange
        var sink = new InMemoryAuditSink();
        var options = new AuditOptions { Enabled = false };
        var logger = CreateLogger([sink], options);
        var evt = CreateTestEvent();

        // Act
        await logger.LogAsync(evt, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(sink.Events);
    }

    [Fact]
    public void ShouldCreateScopeWithCorrelationId_WhenBeginScopeCalled()
    {
        // Arrange
        var logger = CreateLogger();

        // Act
        using var scope = logger.BeginScope("my-correlation-id", "crew-123");

        // Assert
        Assert.Equal("my-correlation-id", scope.CorrelationId);
    }

    [Fact]
    public async Task ShouldEnrichWithCorrelationId_WhenLoggingWithinScope()
    {
        // Arrange
        var sink = new InMemoryAuditSink();
        var logger = CreateLogger([sink]);

        // Act
        using var scope = logger.BeginScope("scope-corr-id", "crew-99");
        await scope.LogAsync(CreateTestEvent(), TestContext.Current.CancellationToken);

        // Assert
        var logged = sink.Events.First();
        Assert.Equal("scope-corr-id", logged.CorrelationId);
        Assert.Equal("crew-99", logged.CrewId);
    }

    [Fact]
    public async Task ShouldDelegateToQueryableSink_WhenQuerying()
    {
        // Arrange
        var sink = new InMemoryAuditSink();
        var evt = CreateTestEvent();
        await sink.WriteAsync(evt, TestContext.Current.CancellationToken);

        var logger = CreateLogger([sink]);
        var query = new AuditQuery();

        // Act
        var results = await logger.QueryAsync(query, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(results);
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenNoQueryableSinkExists()
    {
        // Arrange
        var nonQueryableSink = new MockAuditSink();
        var logger = CreateLogger([nonQueryableSink]);

        // Act
        var results = await logger.QueryAsync(new AuditQuery(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task ShouldNotThrow_WhenSinksListIsEmpty()
    {
        // Arrange
        var logger = CreateLogger(Array.Empty<IAuditSink>());
        var evt = CreateTestEvent();

        // Act & Assert
        var exception = await Record.ExceptionAsync(async () => await logger.LogAsync(evt, TestContext.Current.CancellationToken));
        Assert.Null(exception);
    }

    [Fact]
    public async Task ShouldFilterByCategory_WhenEnabledCategoriesAreConfigured()
    {
        // Arrange
        var sink = new InMemoryAuditSink();
        var options = new AuditOptions
        {
            EnabledCategories = { AuditCategory.SecurityEvent }
        };
        var logger = CreateLogger([sink], options);

        var llmEvent = CreateTestEvent(category: AuditCategory.LlmCall);
        var securityEvent = CreateTestEvent(category: AuditCategory.SecurityEvent);

        // Act
        await logger.LogAsync(llmEvent, TestContext.Current.CancellationToken);
        await logger.LogAsync(securityEvent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(sink.Events);
        Assert.Equal(AuditCategory.SecurityEvent, sink.Events.First().Category);
    }

    [Fact]
    public async Task ShouldAllowAllCategories_WhenEnabledCategoriesIsEmpty()
    {
        // Arrange
        var sink = new InMemoryAuditSink();
        var options = new AuditOptions();
        var logger = CreateLogger([sink], options);

        var llmEvent = CreateTestEvent(category: AuditCategory.LlmCall);
        var securityEvent = CreateTestEvent(category: AuditCategory.SecurityEvent);

        // Act
        await logger.LogAsync(llmEvent, TestContext.Current.CancellationToken);
        await logger.LogAsync(securityEvent, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, sink.Events.Count);
    }
}
