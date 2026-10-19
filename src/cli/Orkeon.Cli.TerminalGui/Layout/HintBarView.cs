using Orkeon.Cli.Abstractions.Runners;
using Orkeon.Cli.TerminalGui.Hosting;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// The one-row bottom hint bar (PLAN C8), replacing Terminal.Gui's StatusBar:
/// <c> ▶▶ default (shift+tab to cycle) · esc to interrupt · ctrl+g logs · ↓ to manage   /rc</c>.
/// Contextual like the reference — the interrupt/agents entries appear only mid-turn.
/// </summary>
/// <remarks>
/// Three labels, three attributes: the chevrons+posture (magenta when the posture is
/// loud), the action hints (dim), and the right-aligned config witness (green when a
/// usable LLM config was loaded, red-leaning accent otherwise). A single-label rendering
/// would flatten the reference's most recognisable line into one color.
/// </remarks>
public sealed class HintBarView : View
{
    private readonly Label _posture;
    private readonly Label _hints;
    private readonly Label _witness;
    private readonly IUiDispatcher _dispatcher;
    private readonly GlyphSet _glyphs;
    private IInteractiveRunner? _runner;
    private TuiIntegration? _integration;
    private bool _agentsAvailable;
    private Func<bool>? _agentsHasRows;
    private Func<ModelWitnessInfo?>? _modelWitness;
    private Func<InputNavigationStateMachine?>? _navigation;
    private int _witnessLength = -1;
    private object? _timerToken;

    public HintBarView(TerminalGuiOptions options)
        : this(options, TerminalGuiDispatcher.Instance) { }

    internal HintBarView(TerminalGuiOptions options, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(options);
        _dispatcher = dispatcher;
        _glyphs = GlyphSet.Resolve(options.Glyphs, OutputIsUtf8());
        Height = 1;
        Width = Dim.Fill();
        SetScheme(SchemeFactory.Pane());

        _posture = new Label { X = 0, Y = 0, Height = 1, Text = string.Empty };
        _posture.SetScheme(SchemeFactory.Dim());
        _hints = new Label { X = Pos.Right(_posture), Y = 0, Height = 1, Text = string.Empty };
        _hints.SetScheme(SchemeFactory.Dim());
        _witness = new Label
        {
            Y = 0,
            Height = 1,
        };
        SetWitness(null);
        Add(_posture, _hints, _witness);
    }

    /// <summary>
    /// Wires the sources and starts the 1 Hz refresh (contextual entries track the turn).
    /// <paramref name="agentsHasRows"/> makes the agents hint honest: "f4 agents" only
    /// shows while the pane actually has rows to select.
    /// </summary>
    public void Bind(
        IInteractiveRunner runner,
        TuiIntegration integration,
        bool agentsAvailable,
        Func<ModelWitnessInfo?>? modelWitness,
        Func<bool>? agentsHasRows = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(integration);
        _runner = runner;
        _integration = integration;
        _agentsAvailable = agentsAvailable;
        _agentsHasRows = agentsHasRows;
        _modelWitness = modelWitness;
        StartTimer();
        Refresh();
    }

    /// <summary>The prompt's navigation state, read at each refresh for the tasks affordance and the recipient label.</summary>
    internal void BindNavigation(Func<InputNavigationStateMachine?> navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        _navigation = navigation;
    }

    internal static bool IsTurnRunning(IInteractiveRunner? runner, TuiIntegration? integration)
        => runner?.IsCommandRunning == true || integration?.PromptTurnRunning?.Invoke() == true;

    /// <summary>Recomposes the bar from the current mode + turn state.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Host-supplied delegate fault barrier: a throwing mode provider degrades the posture to 'default', never crashes the UI.")]
    public void Refresh()
    {
        string mode;
        try
        {
            mode = _integration?.PermissionMode?.Invoke() ?? "default";
        }
        catch
        {
            mode = "default";
        }
        ModelWitnessInfo? witness;
        try
        {
            witness = _modelWitness?.Invoke();
        }
        catch
        {
            witness = null;
        }
        var running = IsTurnRunning(_runner, _integration);
        var agents = _agentsAvailable && (_agentsHasRows?.Invoke() ?? true);
        var navigation = _navigation?.Invoke();
        var segments = HintBarModel.LeftSegments(
            mode,
            running,
            agents,
            taskListFocused: navigation?.State == InputNavigationState.List,
            recipient: navigation?.RecipientName);

        var postureText = $" {_glyphs.Chevrons} {segments[0]}";
        var rest = segments.Count > 1
            ? $" {_glyphs.Dot} " + string.Join($" {_glyphs.Dot} ", segments.Skip(1))
            : string.Empty;

        _dispatcher.Invoke(() =>
        {
            _posture.Text = postureText;
            _posture.SetScheme(HintBarModel.IsLoudPosture(mode) ? SchemeFactory.Posture() : SchemeFactory.Dim());
            _hints.Text = rest;
            SetWitness(witness);
        });
    }

    private void SetWitness(ModelWitnessInfo? info)
    {
        var text = HintBarModel.Witness(info);
        if (text.Length != _witnessLength)
        {
            _witnessLength = text.Length;
            _witness.X = Pos.AnchorEnd(text.Length + 1);
        }
        _witness.Text = text;
        _witness.SetScheme(HintBarModel.WitnessScheme(info?.Availability ?? Orkeon.Cli.Abstractions.ModelAvailability.Unavailable));
    }

    private void StartTimer()
    {
        if (_timerToken is not null) return;
        try
        {
            _timerToken = Application.AddTimeout(TimeSpan.FromSeconds(1), () =>
            {
                Refresh();
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

    /// <summary>Test-only accessor for the composed left text.</summary>
    internal string CurrentText => _posture.Text + _hints.Text;

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
            _posture.Dispose();
            _hints.Dispose();
            _witness.Dispose();
        }
        base.Dispose(disposing);
    }
}
