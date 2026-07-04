using System.Diagnostics.Metrics;
using Orkeon.Infrastructure.Telemetry;

namespace Orkeon.Infrastructure.Tests.Telemetry;

public sealed class OrkeonMetricsTestsFixture : IDisposable
{
    private readonly OrkeonMetrics _metrics;
    private readonly MeterListener _listener;
    private readonly Dictionary<string, List<object>> _measurements = [];

    public OrkeonMetricsTestsFixture()
    {
        _metrics = new OrkeonMetrics();
        _listener = new MeterListener();

        _listener.InstrumentPublished = (instrument, listener) =>
        {
            // Filter by THIS fixture's Meter instance, not by name: parallel test classes
            // create their own OrkeonMetrics sharing OrkeonMetrics.MeterName, and a name-only
            // filter would capture their measurements too (cross-test pollution → flaky counts).
            if (ReferenceEquals(instrument.Meter, _metrics.Meter))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (!_measurements.ContainsKey(instrument.Name))
                _measurements[instrument.Name] = [];
            _measurements[instrument.Name].Add(measurement);
        });

        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            if (!_measurements.ContainsKey(instrument.Name))
                _measurements[instrument.Name] = [];
            _measurements[instrument.Name].Add(measurement);
        });

        _listener.Start();
    }

    public OrkeonMetricsTestsFixture RecordLlmCall(string provider, string model, double durationMs = 0,
        int promptTokens = 0, int completionTokens = 0, double costUsd = 0)
    {
        _metrics.RecordLlmCall(provider, model, durationMs: durationMs,
            promptTokens: promptTokens, completionTokens: completionTokens, costUsd: costUsd);
        _listener.RecordObservableInstruments();
        return this;
    }

    public OrkeonMetricsTestsFixture RecordToolExecution(string toolName, double durationMs, bool success, string agentRole)
    {
        _metrics.RecordToolExecution(toolName, durationMs: durationMs, success: success, agentRole: agentRole);
        _listener.RecordObservableInstruments();
        return this;
    }

    public OrkeonMetricsTestsFixture RecordTaskExecution(string taskId, double durationMs, bool success, string agentRole)
    {
        _metrics.RecordTaskExecution(taskId, durationMs: durationMs, success: success, agentRole: agentRole);
        _listener.RecordObservableInstruments();
        return this;
    }

    public OrkeonMetricsTestsFixture RecordCrewExecution(string crewId, double durationMs, bool success, string processType)
    {
        _metrics.RecordCrewExecution(crewId, durationMs: durationMs, success: success, processType: processType);
        _listener.RecordObservableInstruments();
        return this;
    }

    public OrkeonMetricsTestsFixture CrewStarted(string crewId)
    {
        _metrics.CrewStarted(crewId);
        return this;
    }

    public OrkeonMetricsTestsFixture CrewCompleted(string crewId)
    {
        _metrics.CrewCompleted(crewId);
        return this;
    }

    public OrkeonMetricsTestsFixture TaskStarted(string taskId)
    {
        _metrics.TaskStarted(taskId);
        return this;
    }

    public OrkeonMetricsTestsFixture TaskCompleted(string taskId)
    {
        _metrics.TaskCompleted(taskId);
        return this;
    }

    public OrkeonMetricsTestsFixture LlmCallStarted(string provider)
    {
        _metrics.LlmCallStarted(provider);
        return this;
    }

    public OrkeonMetricsTestsFixture LlmCallCompleted(string provider)
    {
        _metrics.LlmCallCompleted(provider);
        return this;
    }

    public OrkeonMetricsTestsFixture RecordSecurityEvent(string eventType)
    {
        _metrics.RecordSecurityEvent(eventType);
        return this;
    }

    public OrkeonMetricsTestsFixture FlushObservable()
    {
        _listener.RecordObservableInstruments();
        return this;
    }

    public Dictionary<string, List<object>> GetMeasurements() => _measurements;

    public bool HasMeasurement(string name) => _measurements.ContainsKey(name);

    public List<object> GetMeasurement(string name) => _measurements[name];

    public OrkeonMetrics GetMetrics() => _metrics;

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _listener.Dispose();
        _metrics.Dispose();
    }
}
