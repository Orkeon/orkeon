using System.Diagnostics.Metrics;

namespace Orkeon.Tests.Shared.Telemetry;

/// <summary>
/// Records the measurements of one meter, for a test that asserts on metrics (GAP-46). A
/// <see cref="MeterListener"/> hears the whole process, the neighbours' measurements included:
/// a test keeps to its own through a tag only it carries (a crew name, a tool name), either at
/// construction or when it reads. Measurements are added under a lock and handed out as copies.
/// </summary>
public sealed class MeterRecorder : IDisposable
{
    /// <summary>One measurement: its instrument, unit, value and tags (values as text).</summary>
    public sealed record Measurement(string Instrument, string? Unit, double Value, IReadOnlyDictionary<string, string?> Tags)
    {
        /// <summary>The value of the tag <paramref name="key"/>, or <c>null</c> when absent.</summary>
        public string? Tag(string key) => Tags.TryGetValue(key, out var value) ? value : null;
    }

    private readonly MeterListener _listener = new();
    private readonly Lock _gate = new();
    private readonly List<Measurement> _all = [];
    private readonly Dictionary<string, string?> _units = [];
    private readonly string? _keepKey;
    private readonly HashSet<string> _keepValues;

    /// <summary>
    /// Starts recording the meter <paramref name="meterName"/>. With <paramref name="keepKey"/>, only
    /// the measurements whose tag <paramref name="keepKey"/> is one of <paramref name="keepValues"/> are kept.
    /// </summary>
    public MeterRecorder(string meterName, string? keepKey = null, params string[] keepValues)
    {
        ArgumentNullException.ThrowIfNull(meterName);
        _keepKey = keepKey;
        _keepValues = new HashSet<string>(keepValues, StringComparer.Ordinal);
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name != meterName)
                return;
            lock (_gate)
                _units[instrument.Name] = instrument.Unit;
            listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((i, v, t, _) => Add(i, v, t));
        _listener.SetMeasurementEventCallback<double>((i, v, t, _) => Add(i, v, t));
        _listener.Start();
    }

    /// <summary>Every measurement kept so far.</summary>
    public IReadOnlyList<Measurement> All
    {
        get { lock (_gate) return [.. _all]; }
    }

    /// <summary>The measurements of the instrument <paramref name="instrument"/>.</summary>
    public List<Measurement> Of(string instrument) => [.. All.Where(m => m.Instrument == instrument)];

    /// <summary>The measurements of <paramref name="instrument"/> whose tag <paramref name="key"/> is <paramref name="value"/>.</summary>
    public List<Measurement> For(string instrument, string key, string value) =>
        [.. Of(instrument).Where(m => m.Tag(key) == value)];

    /// <summary>The unit the instrument <paramref name="instrument"/> was published with.</summary>
    public string? UnitOf(string instrument)
    {
        lock (_gate)
            return _units.GetValueOrDefault(instrument);
    }

    /// <inheritdoc />
    public void Dispose() => _listener.Dispose();

    private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var map = new Dictionary<string, string?>();
        foreach (var tag in tags)
            map[tag.Key] = tag.Value?.ToString();
        if (_keepKey is not null && (!map.TryGetValue(_keepKey, out var kept) || kept is null || !_keepValues.Contains(kept)))
            return;
        lock (_gate)
            _all.Add(new Measurement(instrument.Name, instrument.Unit, value, map));
    }
}
