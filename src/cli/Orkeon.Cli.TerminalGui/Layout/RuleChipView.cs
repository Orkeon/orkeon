using Orkeon.Cli.TerminalGui.Hosting;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// The one-row horizontal rule with the right-aligned context chip sitting ON it
/// (PLAN C6). The rule takes the CHIP's color — yellow when the prompt targets an
/// agent, blue for the session — exactly the coupling the two captures show.
/// </summary>
public sealed class RuleChipView : View
{
    private readonly Label _rule;
    private readonly Label _chip;
    private readonly IUiDispatcher _dispatcher;
    private readonly GlyphSet _glyphs;
    private TuiIntegration? _integration;

    public RuleChipView(TerminalGuiOptions options)
        : this(options, TerminalGuiDispatcher.Instance) { }

    internal RuleChipView(TerminalGuiOptions options, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(options);
        _dispatcher = dispatcher;
        _glyphs = GlyphSet.Resolve(options.Glyphs, OutputIsUtf8());
        Height = 1;
        Width = Dim.Fill();
        SetScheme(SchemeFactory.Pane());

        _rule = new Label { X = 0, Y = 0, Width = Dim.Fill(), Height = 1, Text = string.Empty };
        _rule.SetScheme(SchemeFactory.Rule(agentTarget: false));
        _chip = new Label { Y = 0, Height = 1, Text = string.Empty, Visible = false };
        _chip.SetScheme(SchemeFactory.Chip(agentTarget: false));
        Add(_rule, _chip);

        // The rule must span whatever width the layout settles on; recompute on layout.
        SubViewLayout += (_, _) => RedrawRule();
    }

    /// <summary>Wires the chip source. The view refreshes on demand (turn boundaries).</summary>
    public void Bind(TuiIntegration integration)
    {
        ArgumentNullException.ThrowIfNull(integration);
        _integration = integration;
        Refresh();
    }

    /// <summary>Re-reads the chip info and repaints rule + chip.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031", Justification = "Host-supplied delegate fault barrier: a throwing chip provider degrades to a bare rule, never crashes the UI.")]
    public void Refresh()
    {
        ContextChipInfo? info;
        try
        {
            info = _integration?.ContextChip?.Invoke();
        }
        catch
        {
            info = null;
        }
        _dispatcher.Invoke(() =>
        {
            if (info is null || string.IsNullOrWhiteSpace(info.Text))
            {
                _chip.Visible = false;
                _rule.SetScheme(SchemeFactory.Rule(agentTarget: false));
            }
            else
            {
                _chip.Text = $" {info.Text} ";
                _chip.SetScheme(SchemeFactory.Chip(info.IsAgentTarget));
                _chip.X = Pos.AnchorEnd(info.Text.Length + 2);
                _chip.Visible = true;
                _rule.SetScheme(SchemeFactory.Rule(info.IsAgentTarget));
            }
            RedrawRule();
        });
    }

    private void RedrawRule()
    {
        var width = Math.Max(0, Viewport.Width);
        var cell = _glyphs.RuleCell.Length > 0 ? _glyphs.RuleCell[0] : '-';
        _rule.Text = new string(cell, width);
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

    /// <summary>Test-only accessor for the chip text ("" when hidden).</summary>
    internal string CurrentChip => _chip.Visible ? _chip.Text : string.Empty;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rule.Dispose();
            _chip.Dispose();
        }
        base.Dispose(disposing);
    }
}
