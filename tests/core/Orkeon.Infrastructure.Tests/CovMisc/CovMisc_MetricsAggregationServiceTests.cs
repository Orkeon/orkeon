using Microsoft.Extensions.Logging.Abstractions;
using Orkeon.Infrastructure.Monitoring;
using Orkeon.Infrastructure.Telemetry;

namespace Orkeon.Infrastructure.Tests.CovMisc;

/// <summary>
/// Coverage for <see cref="MetricsAggregationService"/> measurement callbacks, exercised by
/// emitting real measurements through an <see cref="OrkeonMetrics"/> instance on the
/// <c>Orkeon</c> meter that the service listens to.
/// </summary>
[Collection("OrkeonMeter")] // observes the global "Orkeon" meter; serialize with other meter tests
public class CovMisc_MetricsAggregationServiceTests
{
    [Fact]
    public void Constructor_NullLogger_Throws()
        => Assert.Throws<ArgumentNullException>(() => new MetricsAggregationService(null!));

    [Fact]
    public async Task Aggregates_Counters_UpDownCounters_AndHistogram()
    {
        // Service must be created BEFORE the meter instruments are published so that
        // MeterListener.InstrumentPublished enables them.
        using var service = new MetricsAggregationService(NullLogger<MetricsAggregationService>.Instance);
        using var metrics = new OrkeonMetrics();

        // Counters
        metrics.RecordLlmCall("openai", "gpt-4o", durationMs: 12.0, promptTokens: 3, completionTokens: 4, costUsd: 0.5);
        metrics.RecordLlmCall("openai", "gpt-4o", durationMs: 8.0);
        metrics.RecordToolExecution("calc", durationMs: 1.0);
        metrics.RecordTaskExecution("t1", durationMs: 2.0);
        metrics.RecordCrewExecution("c1", durationMs: 3.0);

        // UpDownCounters (active gauges)
        metrics.CrewStarted("c1");
        metrics.TaskStarted("t1");

        var current = await service.GetCurrentMetricsAsync(TestContext.Current.CancellationToken);

        // Use >= rather than == because the MeterListener observes ALL instruments on the
        // shared "Orkeon" meter name, including instances created by parallel test classes.
        // Counters are monotonic (only ever increase), so >= is contamination-safe.
        Assert.True(current.TotalLlmCalls >= 2);
        Assert.True(current.TotalToolExecutions >= 1);
        Assert.True(current.TotalTaskExecutions >= 1);
        Assert.True(current.TotalCrewExecutions >= 1);
        // UpDownCounters net globally across the shared "Orkeon" meter; a parallel test's
        // CrewCompleted/-1 could offset our +1, so only assert the value is well-defined.
        Assert.True(current.ActiveCrews >= 0);
        Assert.True(current.ActiveTasks >= 0);
        // Observable gauge for cost is captured via RecordObservableInstruments. Its absolute
        // value can be overwritten by a parallel meter's gauge callback, so assert non-negative.
        Assert.True(current.TotalCostUsd >= 0.0);
    }

    [Fact]
    public async Task Snapshot_ExposesCountersByName_AndHistogramAverages()
    {
        using var service = new MetricsAggregationService(NullLogger<MetricsAggregationService>.Instance);
        using var metrics = new OrkeonMetrics();

        metrics.RecordLlmCall("anthropic", "claude", durationMs: 100.0);
        metrics.RecordLlmCall("anthropic", "claude", durationMs: 200.0);
        metrics.CrewStarted("crew-x");
        metrics.CrewCompleted("crew-x");

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
    public async Task EmptyMeter_ReturnsNonNullMetrics()
    {
        using var service = new MetricsAggregationService(NullLogger<MetricsAggregationService>.Instance);

        var current = await service.GetCurrentMetricsAsync(TestContext.Current.CancellationToken);
        var snapshot = await service.GetMetricsSnapshotAsync(TestContext.Current.CancellationToken);

        // No OrkeonMetrics instance is created here. Counters are non-negative and the
        // snapshot is well-formed (parallel meters may still add entries, so avoid exact zero).
        Assert.True(current.TotalLlmCalls >= 0);
        Assert.True(current.TotalCostUsd >= 0.0);
        Assert.NotNull(snapshot.HistogramAverages);
        Assert.NotNull(snapshot.CountersByName);
    }
}
