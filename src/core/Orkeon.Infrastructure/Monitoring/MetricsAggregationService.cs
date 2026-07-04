using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Orkeon.Application.Interfaces.Monitoring;
using Orkeon.Infrastructure.Telemetry;
using Microsoft.Extensions.Logging;

namespace Orkeon.Infrastructure.Monitoring;

/// <summary>
/// Captures metrics from the <c>Orkeon</c> meter using a <see cref="MeterListener"/>
/// and exposes them as aggregated snapshots.
/// </summary>
public sealed partial class MetricsAggregationService : IMetricsAggregation, IDisposable
{
    private readonly ILogger<MetricsAggregationService> _logger;
    private readonly MeterListener _meterListener;

    // Stores cumulative counter values keyed by instrument name.
    private readonly ConcurrentDictionary<string, long> _counters = new();

    // Stores UpDownCounter values keyed by instrument name.
    private readonly ConcurrentDictionary<string, long> _upDownCounters = new();

    // Stores histogram samples: each instrument tracks (sum, count) for averages.
    private readonly ConcurrentDictionary<string, HistogramAccumulator> _histograms = new();

    /// <summary>
    /// Initializes a new instance and starts listening to the <c>Orkeon</c> meter.
    /// </summary>
    public MetricsAggregationService(ILogger<MetricsAggregationService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _meterListener = new MeterListener();

        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == OrkeonMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _meterListener.SetMeasurementEventCallback<long>(OnLongMeasurement);
        _meterListener.SetMeasurementEventCallback<double>(OnDoubleMeasurement);

        _meterListener.Start();

        LogMetricsaggregationserviceStartedListeningToMeter(OrkeonMetrics.MeterName);
    }

    /// <inheritdoc />
    public Task<AggregatedMetrics> GetCurrentMetricsAsync(CancellationToken ct = default)
    {
        // Flush any buffered measurements before reading
        _meterListener.RecordObservableInstruments();

        var metrics = new AggregatedMetrics
        {
            TotalLlmCalls = GetCounter("orkeon.llm.calls"),
            TotalToolExecutions = GetCounter("orkeon.tool.executions"),
            TotalTaskExecutions = GetCounter("orkeon.task.executions"),
            TotalCrewExecutions = GetCounter("orkeon.crew.executions"),
            ActiveCrews = GetUpDownCounter("orkeon.crew.active"),
            ActiveTasks = GetUpDownCounter("orkeon.task.active"),
            TotalCostUsd = GetHistogramSum("orkeon.cost.total_usd"),
            CapturedAt = DateTime.UtcNow
        };

        return Task.FromResult(metrics);
    }

    /// <inheritdoc />
    public Task<MetricsSnapshot> GetMetricsSnapshotAsync(CancellationToken ct = default)
    {
        _meterListener.RecordObservableInstruments();

        var allCounters = new Dictionary<string, long>();
        foreach (var kvp in _counters)
            allCounters[kvp.Key] = kvp.Value;
        foreach (var kvp in _upDownCounters)
            allCounters[kvp.Key] = kvp.Value;

        var histogramAverages = new Dictionary<string, double>();
        foreach (var kvp in _histograms)
        {
            var acc = kvp.Value;
            histogramAverages[kvp.Key] = acc.Count > 0 ? acc.Sum / acc.Count : 0.0;
        }

        var snapshot = new MetricsSnapshot
        {
            Metrics = new AggregatedMetrics
            {
                TotalLlmCalls = GetCounter("orkeon.llm.calls"),
                TotalToolExecutions = GetCounter("orkeon.tool.executions"),
                TotalTaskExecutions = GetCounter("orkeon.task.executions"),
                TotalCrewExecutions = GetCounter("orkeon.crew.executions"),
                ActiveCrews = GetUpDownCounter("orkeon.crew.active"),
                ActiveTasks = GetUpDownCounter("orkeon.task.active"),
                TotalCostUsd = GetHistogramSum("orkeon.cost.total_usd"),
                CapturedAt = DateTime.UtcNow
            },
            CountersByName = allCounters,
            HistogramAverages = histogramAverages,
            SnapshotAt = DateTime.UtcNow
        };

        return Task.FromResult(snapshot);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _meterListener.Dispose();
        LogMetricsaggregationserviceDisposed();
    }

    private void OnLongMeasurement(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        var name = instrument.Name;

        if (instrument is UpDownCounter<long>)
        {
            _upDownCounters.AddOrUpdate(name, measurement, (_, existing) => existing + measurement);
        }
        else if (instrument is ObservableGauge<long>)
        {
            // Observable gauges report absolute values
            _upDownCounters[name] = measurement;
        }
        else
        {
            // Counter<long>
            _counters.AddOrUpdate(name, measurement, (_, existing) => existing + measurement);
        }
    }

    private void OnDoubleMeasurement(
        Instrument instrument,
        double measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        var name = instrument.Name;

        if (instrument is Histogram<double>)
        {
            _histograms.AddOrUpdate(
                name,
                new HistogramAccumulator { Sum = measurement, Count = 1 },
                (_, existing) =>
                {
                    existing.Sum += measurement;
                    existing.Count++;
                    return existing;
                });
        }
        else if (instrument is ObservableGauge<double>)
        {
            // Observable gauges report absolute values; store as histogram for GetHistogramSum
            _histograms.AddOrUpdate(
                name,
                new HistogramAccumulator { Sum = measurement, Count = 1 },
                (_, existing) =>
                {
                    existing.Sum = measurement;
                    existing.Count = 1;
                    return existing;
                });
        }
    }

    private long GetCounter(string name) =>
        _counters.TryGetValue(name, out var value) ? value : 0;

    private long GetUpDownCounter(string name) =>
        _upDownCounters.TryGetValue(name, out var value) ? value : 0;

    private double GetHistogramSum(string name) =>
        _histograms.TryGetValue(name, out var acc) ? acc.Sum : 0.0;

    /// <summary>
    /// Mutable accumulator for histogram sum and count. Thread safety is ensured
    /// by ConcurrentDictionary's AddOrUpdate semantics.
    /// </summary>
    private sealed class HistogramAccumulator
    {
        public double Sum;
        public long Count;
    }

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "MetricsAggregationService started listening to meter '{MeterName}'")]
    private partial void LogMetricsaggregationserviceStartedListeningToMeter(object meterName);

    [LoggerMessage(Level = Microsoft.Extensions.Logging.LogLevel.Debug, Message = "MetricsAggregationService disposed")]
    private partial void LogMetricsaggregationserviceDisposed();

}
