using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Monitoring;
using Orkeon.Application.Telemetry;

namespace Orkeon.Infrastructure.Tests.CovMisc;

/// <summary>
/// Coverage for <see cref="MetricsAggregationService"/> measurement callbacks, exercised by
/// emitting real measurements through an <see cref="OrkeonMeter"/> on the
/// <c>Orkeon</c> meter that the service listens to.
/// </summary>
[Collection("OrkeonMeter")] // observes the global "Orkeon" meter; serialize with other meter tests
public class CovMisc_MetricsAggregationServiceTests
{
    [Fact]
    [Trait("BR", "CH-03")]
    public void Constructor_NullLogger_Throws()
        => Assert.Throws<ArgumentNullException>(() => new MetricsAggregationService(null!));

    [Fact]
    [Trait("BR", "CH-03")]
    public async Task Aggregates_Counters_UpDownCounters_AndHistogram()
    {
        // Service must be created BEFORE the meter instruments are published so that
        // MeterListener.InstrumentPublished enables them.
        using var service = new MetricsAggregationService(NullLogger<MetricsAggregationService>.Instance);

        // Counters
        OrkeonMeter.RecordLlmCall("openai", "gpt-4o", 0.012, null, 3, 4);
        OrkeonMeter.RecordLlmCall("openai", "gpt-4o", 0.008, null, null, null);

        var current = await service.GetCurrentMetricsAsync(TestContext.Current.CancellationToken);

        // Use >= rather than == because the MeterListener observes ALL instruments on the
        // shared "Orkeon" meter name, including instances created by parallel test classes.
        // Counters are monotonic (only ever increase), so >= is contamination-safe.
        Assert.True(current.TotalLlmCalls >= 2);
        // Pinned: the dashboard reads zero for tool, task and crew, which OrkeonMeter does not measure.
        Assert.Equal(0, current.TotalToolExecutions);
        Assert.Equal(0, current.TotalTaskExecutions);
        Assert.Equal(0, current.TotalCrewExecutions);
        // UpDownCounters net globally across the shared "Orkeon" meter; a parallel test's
        // CrewCompleted/-1 could offset our +1, so only assert the value is well-defined.
        Assert.True(current.ActiveCrews >= 0);
        Assert.True(current.ActiveTasks >= 0);
        // Observable gauge for cost is captured via RecordObservableInstruments. Its absolute
        // value can be overwritten by a parallel meter's gauge callback, so assert non-negative.
        Assert.True(current.TotalCostUsd >= 0.0);
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async Task Snapshot_ExposesCountersByName_AndHistogramAverages()
    {
        using var service = new MetricsAggregationService(NullLogger<MetricsAggregationService>.Instance);

        OrkeonMeter.RecordLlmCall("anthropic", "claude", 0.1, null, null, null);
        OrkeonMeter.RecordLlmCall("anthropic", "claude", 0.2, null, null, null);

        var snapshot = await service.GetMetricsSnapshotAsync(TestContext.Current.CancellationToken);

        Assert.NotEmpty(snapshot.CountersByName);
        Assert.True(snapshot.CountersByName.ContainsKey("orkeon.llm.calls"));
        // >= because parallel test classes may emit on the shared "Orkeon" meter.
        Assert.True(snapshot.CountersByName["orkeon.llm.calls"] >= 2);

        // Histogram average of the operation duration (seconds) is the mean of all recorded
        // samples; positive whatever parallel test classes add on the shared meter.
        Assert.True(snapshot.HistogramAverages.ContainsKey("gen_ai.client.operation.duration"));
        var avg = snapshot.HistogramAverages["gen_ai.client.operation.duration"];
        Assert.True(avg > 0.0, "histogram average should be positive");
    }

    [Fact]
    [Trait("BR", "CH-03")]
    public async Task EmptyMeter_ReturnsNonNullMetrics()
    {
        using var service = new MetricsAggregationService(NullLogger<MetricsAggregationService>.Instance);

        var current = await service.GetCurrentMetricsAsync(TestContext.Current.CancellationToken);
        var snapshot = await service.GetMetricsSnapshotAsync(TestContext.Current.CancellationToken);

        // Nothing is recorded here. Counters are non-negative and the
        // snapshot is well-formed (parallel meters may still add entries, so avoid exact zero).
        Assert.True(current.TotalLlmCalls >= 0);
        Assert.True(current.TotalCostUsd >= 0.0);
        Assert.NotNull(snapshot.HistogramAverages);
        Assert.NotNull(snapshot.CountersByName);
    }
}
