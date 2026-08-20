using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orkeon.Infrastructure.Monitoring;

namespace Orkeon.Infrastructure.Tests.Monitoring;

/// <summary>
/// Fixture providing test doubles for <see cref="TraceExplorerServiceTests"/>.
/// </summary>
public sealed class TraceExplorerServiceTestsFixture : IDisposable
{
    public ILogger<TraceExplorerService> Logger { get; }

    public TraceExplorerServiceTestsFixture()
    {
        Logger = new NullLogger();
    }

    /// <summary>
    /// Builds a service listening to <paramref name="sourcePrefix"/>. The listener is
    /// process-wide, so a test asserting "no traces" against the default <c>Orkeon</c> prefix
    /// is really asserting that no other test in the assembly emitted one — which is not true
    /// and became less true as the suite grew.
    /// </summary>
    public TraceExplorerService CreateService(int maxTraceHistory = 1000, string sourcePrefix = "Orkeon") =>
        new(Logger, new OptionsWrapper(new MonitoringOptions
        {
            MaxTraceHistory = maxTraceHistory,
            TraceSourcePrefix = sourcePrefix,
        }));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A no-op logger for test purposes.
    /// </summary>
    private sealed class NullLogger : ILogger<TraceExplorerService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    /// <summary>
    /// Simple IOptions wrapper for test purposes.
    /// </summary>
    private sealed class OptionsWrapper : IOptions<MonitoringOptions>
    {
        public MonitoringOptions Value { get; }

        public OptionsWrapper(MonitoringOptions value)
        {
            Value = value;
        }
    }
}
