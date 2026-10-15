using System.Diagnostics.Metrics;

namespace Orkeon.Application.Tests.Telemetry;

/// <summary>Records every measurement of the "Orkeon" meter: instrument, unit, value, tags.</summary>
internal sealed class OrkeonMeterRecorder : IDisposable
{
    internal sealed record Measurement(string Instrument, string? Unit, double Value, IReadOnlyDictionary<string, object?> Tags);

    private readonly MeterListener _listener = new();
    private readonly List<Measurement> _measurements = [];
    private readonly Lock _gate = new();

    public OrkeonMeterRecorder()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Orkeon")
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Add(i, v, t));
        _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Add(i, v, t));
        _listener.Start();
    }

    /// <summary>The measurements of one instrument whose <c>orkeon.tool.name</c> tag is <paramref name="toolName"/>.</summary>
    public List<Measurement> For(string instrument, string toolName)
    {
        lock (_gate)
            return _measurements
                .Where(m => m.Instrument == instrument
                    && m.Tags.TryGetValue("orkeon.tool.name", out var n) && Equals(n, toolName))
                .ToList();
    }

    public void Dispose() => _listener.Dispose();

    private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>();
        foreach (var tag in tags)
            copy[tag.Key] = tag.Value;
        lock (_gate)
            _measurements.Add(new Measurement(instrument.Name, instrument.Unit, value, copy));
    }
}
