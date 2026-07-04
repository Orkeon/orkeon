using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Scripting.Runtime;

#pragma warning disable IDE1006 // intentional camelCase: exposed to JS as ProgressHandle returned by ctx.progress(...)
/// <summary>
/// Lightweight progress reporter — increments by 1, jumps to a value, or finalises.
/// Renders to the console adapter as plain lines that flow into Terminal.Gui's logs pane
/// (no preference for a fancy spinner — keeps the surface deterministic for tests).
/// </summary>
public sealed class ProgressHandle
{
    private readonly IConsoleAdapter _console;
    private readonly string _label;
    private readonly int? _total;
    private int _value;
    private bool _done;

    public ProgressHandle(IConsoleAdapter console, string label, int? total)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _label = label;
        _total = total;
    }

    /// <summary>Increment by 1, optionally setting a new step label.</summary>
    public void advance(string? label = null)
    {
        if (_done) return;
        _value++;
        WriteLine(label, _value);
    }

    /// <summary>Jump to <paramref name="value"/>, optionally setting a step label.</summary>
    public void set(int value, string? label = null)
    {
        if (_done) return;
        _value = value;
        WriteLine(label, value);
    }

    /// <summary>Finalise the progress; subsequent calls are no-ops.</summary>
    public void done(string? label = null)
    {
        if (_done) return;
        _done = true;
        var line = $"[{_label}] done";
        if (!string.IsNullOrEmpty(label)) line += $": {label}";
        _console.WriteLine(line);
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
