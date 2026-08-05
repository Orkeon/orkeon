using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Telemetry;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// The one-row status line above the prompt rule:
/// <c>✱ Reasoning… (3m 44s · ↓ 118.3k tokens · streaming)</c>. Replaces the framed
/// "Running command" bandeau (PLAN phase 3). Blank while idle — the reference clears
/// this row between turns rather than parking an "idle" label on it.
/// </summary>
public sealed class StatusLineView : View
{
    private readonly Label _label;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private readonly GlyphSet _glyphs;
    private object? _timerToken;
    private IInteractiveRunner? _runner;
    private TuiIntegration? _integration;
    private ToolActivityAggregator? _activity;
    private bool _deltasFlowing;

    // Turn tracking: the gerund stays stable per turn, the token readout is a DELTA
    // from the turn's start (the reference counts the turn, not the session).
    private DateTime? _turnStartedAt;
    private long _tokensAtTurnStart;
    private bool _wasRunning;

    public StatusLineView(TerminalGuiOptions options)
        : this(options, TerminalGuiDispatcher.Instance, TimeProvider.System) { }

    internal StatusLineView(TerminalGuiOptions options, IUiDispatcher dispatcher, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        _dispatcher = dispatcher;
        _clock = clock;
        _glyphs = GlyphSet.Resolve(options.Glyphs, OutputIsUtf8());
        Height = 1;
        Width = Dim.Fill();
        SetScheme(SchemeFactory.Accent());
        _label = new Label { X = 0, Y = 0, Width = Dim.Fill(), Height = 1, Text = string.Empty };
        _label.SetScheme(SchemeFactory.Accent());
        Add(_label);
    }

    /// <summary>Raised on the running→idle transition — the transcript flushes tool activity on it.</summary>
    public event EventHandler? TurnCompleted;

    /// <summary>Wires the sources and starts the 1 Hz refresh. Safe to call repeatedly.</summary>
    public void Bind(IInteractiveRunner runner, TuiIntegration? integration, ToolActivityAggregator? activity)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _integration = integration;
        _activity = activity;
        StartTimer();
        Refresh();
    }

    /// <summary>Signal from the streaming sink: content deltas are (not) arriving.</summary>
    public void SetStreaming(bool active) => _deltasFlowing = active;

    /// <summary>Forces a recompute + repaint. Used by the timer and by tests.</summary>
    public void Refresh()
    {
        var text = ComposeText();
        _dispatcher.Invoke(() => _label.Text = text);
    }

    private string ComposeText()
    {
        var runner = _runner;
        var running = runner?.IsCommandRunning == true;

        if (running && !_wasRunning)
        {
            _turnStartedAt = runner!.CurrentCommandStartedAtUtc ?? _clock.GetUtcNow().UtcDateTime;
            _tokensAtTurnStart = ReadSessionTokens() ?? 0;
        }
        if (!running && _wasRunning)
        {
            _turnStartedAt = null;
            _deltasFlowing = false;
            TurnCompleted?.Invoke(this, EventArgs.Empty);
        }
        _wasRunning = running;

        if (!running) return string.Empty;

        var started = _turnStartedAt ?? _clock.GetUtcNow().UtcDateTime;
        var elapsed = _clock.GetUtcNow().UtcDateTime - started;
        var gerund = StatusLineFormatter.GerundFor(started.Ticks);

        long? tokens = ReadSessionTokens() is { } total
            ? Math.Max(0, total - _tokensAtTurnStart)
            : null;

        TurnState? state =
            _activity?.HasOpenToolCall == true ? TurnState.Tool
            : _deltasFlowing ? TurnState.Streaming
            : TurnState.Working;

        return StatusLineFormatter.Compose(_glyphs, gerund, elapsed, tokens, state);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Host-supplied delegate fault barrier: a throwing token provider degrades the readout to omission, never crashes the UI timer.")]
    private long? ReadSessionTokens()
    {
        try
        {
            return _integration?.SessionTokens?.Invoke();
        }
        catch
        {
            return null;
        }
    }

    private void StartTimer()
    {
        if (_timerToken is not null) return;
        try
        {
            _timerToken = Application.AddTimeout(TimeSpan.FromSeconds(1), () =>
            {
                _label.Text = ComposeText();
                return true;
            });
        }
        catch (InvalidOperationException)
        {
            // Application not initialised (unit tests) — Refresh() stays callable manually.
        }
    }

    private static bool OutputIsUtf8()
    {
        try
        {
            return System.Console.OutputEncoding.CodePage is 65001;
        }
        catch (System.IO.IOException)
        {
            return false;
        }
    }

    /// <summary>Test-only accessor for the rendered text.</summary>
    internal string CurrentText => _label.Text;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_timerToken is not null)
            {
                try { Application.RemoveTimeout(_timerToken); }
                catch (InvalidOperationException) { /* Application already shut down */ }
                _timerToken = null;
            }
            _label.Dispose();
        }
        base.Dispose(disposing);
    }
}
