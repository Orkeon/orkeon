using Orkeon.Infrastructure.Monitoring;

namespace Orkeon.Infrastructure.Tests.Monitoring;

[Collection("OrkeonMeter")]
public sealed class MetricsAggregationServiceTests : IClassFixture<MetricsAggregationServiceTestsFixture>, IDisposable
{
    private readonly MetricsAggregationServiceTestsFixture _fixture;
    private readonly MetricsAggregationService _service;

    public MetricsAggregationServiceTests(MetricsAggregationServiceTestsFixture fixture)
    {
        _fixture = fixture;
        _service = _fixture.CreateService();
    }

    [Fact]
    public async Task GetCurrentMetricsAsync_ReturnsDefaultMetrics()
    {
        // Act
        var metrics = await _service.GetCurrentMetricsAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(metrics);
        Assert.True(metrics.CapturedAt <= DateTime.UtcNow);
        Assert.True(metrics.CapturedAt > DateTime.UtcNow.AddSeconds(-5));
        Assert.Equal(0, metrics.TotalLlmCalls);
        Assert.Equal(0, metrics.TotalToolExecutions);
        Assert.Equal(0, metrics.TotalTaskExecutions);
        Assert.Equal(0, metrics.TotalCrewExecutions);
        Assert.Equal(0, metrics.ActiveCrews);
        Assert.Equal(0, metrics.ActiveTasks);
        Assert.Equal(0.0, metrics.TotalCostUsd);
    }

    [Fact]
    public async Task GetMetricsSnapshotAsync_ReturnsSnapshotWithTimestamp()
    {
        // Act
        var before = DateTime.UtcNow;
        var snapshot = await _service.GetMetricsSnapshotAsync(TestContext.Current.CancellationToken);
        var after = DateTime.UtcNow;

        // Assert
        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.Metrics);
        Assert.NotNull(snapshot.CountersByName);
        Assert.NotNull(snapshot.HistogramAverages);
        Assert.True(snapshot.SnapshotAt >= before && snapshot.SnapshotAt <= after,
            "SnapshotAt should be between the before and after timestamps");
    }

    [Fact]
    public void Service_ImplementsIDisposable()
    {
        // Arrange
        using var service = _fixture.CreateService();

        // Act & Assert — should not throw
        var exception = Record.Exception(() => service.Dispose());
        Assert.Null(exception);
    }

    public void Dispose()
    {
        _service.Dispose();
        GC.SuppressFinalize(this);
    }
}
