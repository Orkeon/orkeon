using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.TerminalGui.Hosting;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Single-line bandeau that surfaces the currently running command (name, elapsed
/// wall time, animated spinner). When no command is in flight the line shows an
/// "idle" placeholder. Backed by a 1Hz timer so the elapsed counter stays live.
/// </summary>
public sealed class TasksPaneView : FrameView
{
    private static readonly char[] SpinnerFrames = { '|', '/', '-', '\\' };

    private readonly MouseClipboardTextView _label;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private object? _timerToken;
    private int _spinnerIndex;
    private IInteractiveRunner? _runner;

    public TasksPaneView(TerminalGuiOptions options)
        : this(options, TerminalGuiDispatcher.Instance, TimeProvider.System) { }

    internal TasksPaneView(TerminalGuiOptions options, IUiDispatcher dispatcher, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        _dispatcher = dispatcher;
        _clock = clock;
        Title = options.TasksPaneTitle;
        SetScheme(SchemeFactory.Pane());

        // Backed by a single-line ReadOnly TextView (not a Label) so the user can
        // select the running-command text with the mouse and the auto-copy hook
        // from MouseClipboardTextView pushes it to the OS clipboard on release.
        _label = new MouseClipboardTextView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            Text = IdleText,
            ReadOnly = true,
            Multiline = false,
            WordWrap = false,
            CanFocus = false,
        };
        _label.SetScheme(SchemeFactory.Pane());
        Add(_label);
    }

    /// <summary>
    /// Wires the runner whose command lifecycle this pane reflects. Starts the 1Hz
    /// refresh timer once the runner is set; safe to call multiple times.
    /// </summary>
    public void Bind(IInteractiveRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        StartTimer();
        Refresh();
    }

    /// <summary>Forces an immediate refresh of the bandeau text. Mainly used in tests.</summary>
    public void Refresh()
    {
        var text = ComposeText();
        _dispatcher.Invoke(() => _label.Text = text);
    }

    private string ComposeText()
    {
        var runner = _runner;
        if (runner is null || !runner.IsCommandRunning) return IdleText;

        var name = runner.CurrentCommandName ?? "(unknown)";
        var startedAt = runner.CurrentCommandStartedAtUtc ?? _clock.GetUtcNow().UtcDateTime;
        var elapsed = _clock.GetUtcNow().UtcDateTime - startedAt;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var spinner = SpinnerFrames[_spinnerIndex % SpinnerFrames.Length];
        return $"{spinner} {name} — {TasksPaneFormatter.FormatElapsed(elapsed)}";
    }

    private void StartTimer()
    {
        if (_timerToken is not null) return;
        try
        {
            _timerToken = Application.AddTimeout(TimeSpan.FromSeconds(1), () =>
            {
                _spinnerIndex = (_spinnerIndex + 1) % SpinnerFrames.Length;
                _label.Text = ComposeText();
                return true;
            });
        }
        catch (InvalidOperationException)
        {
            // Application not initialised (e.g. unit tests) — ignore; Refresh() is
            // still callable manually.
        }
    }

    /// <summary>Formats elapsed time as <c>m:ss</c> for short runs and <c>h:mm:ss</c> beyond an hour.</summary>
    internal static string FormatElapsed(TimeSpan elapsed) => TasksPaneFormatter.FormatElapsed(elapsed);

    internal const string IdleText = "  idle — no command running";

    /// <summary>Test-only accessor for the rendered label text.</summary>
    internal string CurrentText => _label.Text;

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Stop the 1Hz refresh callback before tearing down the label it writes to.
            if (_timerToken is not null)
            {
                try { Application.RemoveTimeout(_timerToken); }
                catch (InvalidOperationException) { /* Application already shut down */ }
                _timerToken = null;
            }

            // Terminal.Gui's base View.Dispose also disposes the Add()-ed _label; its
            // IsDisposed guard makes this explicit call an idempotent no-op.
            _label.Dispose();
        }
        base.Dispose(disposing);
    }
}
