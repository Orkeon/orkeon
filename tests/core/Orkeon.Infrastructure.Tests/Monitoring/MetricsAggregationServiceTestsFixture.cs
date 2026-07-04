using Microsoft.Extensions.Logging;

namespace Orkeon.Infrastructure.Tests.Monitoring;

/// <summary>
/// Fixture providing test doubles for <see cref="MetricsAggregationServiceTests"/>.
/// </summary>
public sealed class MetricsAggregationServiceTestsFixture : IDisposable
{
    public ILogger<Orkeon.Infrastructure.Monitoring.MetricsAggregationService> Logger { get; }

    public MetricsAggregationServiceTestsFixture()
    {
        Logger = new NullLogger();
    }

    public Orkeon.Infrastructure.Monitoring.MetricsAggregationService CreateService() =>
        new(Logger);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A no-op logger for test purposes.
    /// </summary>
    private sealed class NullLogger : ILogger<Orkeon.Infrastructure.Monitoring.MetricsAggregationService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
