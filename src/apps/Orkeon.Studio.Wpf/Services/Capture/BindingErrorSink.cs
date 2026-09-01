using System.Diagnostics;
using System.Windows.Diagnostics;

namespace Orkeon.Studio.Wpf.Services.Capture;

/// <summary>
/// Reads the trace nobody reads.
/// <para>
/// WPF answers a broken binding by leaving the target at its default value and writing one line to
/// a trace source. That is how a screen ships with an empty label everybody assumes is a design
/// choice — and it is the only check in the campaign that finds content that is *wrong* rather
/// than content that is *missing*.
/// </para>
/// </summary>
internal sealed class BindingErrorSink : TraceListener
{
    private readonly List<string> _errors = [];

    private BindingErrorSink()
    {
    }

    /// <summary>Attaches a sink to the data-binding trace source and turns the source up.</summary>
    public static BindingErrorSink Install()
    {
        PresentationTraceSources.Refresh();

        var sink = new BindingErrorSink();
        PresentationTraceSources.DataBindingSource.Listeners.Add(sink);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error | SourceLevels.Warning;

        return sink;
    }

    /// <summary>Takes what has accumulated and starts again, so each stop owns its own errors.</summary>
    public IReadOnlyList<string> Drain()
    {
        var drained = _errors.ToArray();
        _errors.Clear();
        return drained;
    }

    /// <inheritdoc />
    public override void Write(string? message) => WriteLine(message);

    /// <inheritdoc />
    public override void WriteLine(string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            _errors.Add(message);
    }
}
