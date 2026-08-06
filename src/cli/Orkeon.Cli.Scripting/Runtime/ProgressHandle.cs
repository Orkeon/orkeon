using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.Scripting.Dispatch;
using Orkeon.Cli.Scripting.Progress;

namespace Orkeon.Cli.Scripting.Runtime;

#pragma warning disable IDE1006 // intentional camelCase: exposed to JS as ProgressHandle returned by ctx.progress(...)
/// <summary>
/// Lightweight progress reporter — increments by 1, jumps to a value, or finalises.
/// Renders to the console adapter as plain lines that flow into Terminal.Gui's logs pane
/// (deterministic surface for tests), and — when the host wired a
/// <see cref="ProgressBroker"/> — also publishes live snapshots for the TUI status-line
/// bar and the ambient <see cref="CommandInstance"/> (ps/inspect, agents pane).
/// </summary>
public sealed class ProgressHandle
{
    private readonly IConsoleAdapter _console;
    private readonly ProgressBroker? _broker;
    private readonly string _label;
    private readonly int? _total;
    private int _value;
    private bool _done;

    public ProgressHandle(IConsoleAdapter console, string label, int? total, ProgressBroker? broker = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _label = label;
        _total = total;
        _broker = broker;
    }

    /// <summary>Increment by 1, optionally setting a new step label.</summary>
    public void advance(string? label = null)
    {
        if (_done) return;
        _value++;
        WriteLine(label, _value);
        Publish(label);
    }

    /// <summary>Jump to <paramref name="value"/>, optionally setting a step label.</summary>
    public void set(int value, string? label = null)
    {
        if (_done) return;
        _value = value;
        WriteLine(label, value);
        Publish(label);
    }

    /// <summary>Finalise the progress; subsequent calls are no-ops.</summary>
    public void done(string? label = null)
    {
        if (_done) return;
        _done = true;
        var line = $"[{_label}] done";
        if (!string.IsNullOrEmpty(label)) line += $": {label}";
        _console.WriteLine(line);
        _broker?.ClearLabel(_label);
    }

    private void Publish(string? stepLabel)
    {
        var instance = ProgressAmbient.CurrentInstance;
        _broker?.Report(new ProgressSnapshot
        {
            Label = _label,
            Step = _value,
            Total = _total,
            Message = stepLabel,
            StartedAt = DateTimeOffset.UtcNow,
            Ticket = instance?.Ticket,
        });
        instance?.ReportProgress(new CommandProgress(_value, percent: null, stepLabel ?? _label));
    }

    private void WriteLine(string? stepLabel, int value)
    {
        var head = _total is { } t
            ? $"[{_label}] {value}/{t}"
            : $"[{_label}] {value}";
        if (!string.IsNullOrEmpty(stepLabel))
            head += $" — {stepLabel}";
        _console.WriteLine(head);
    }
}
#pragma warning restore IDE1006
