using Orkeon.Cli.TerminalGui.Hosting;
using Terminal.Gui.ViewBase;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// A one-row separator framing the prompt: a dim <c>─</c> rule across the full width. The top
/// one carries the prompt's recipient while it is a task other than <c>main</c>:
/// <c>────── worker@crew ─</c>, the label in the accent (<see cref="SeparatorModel"/>). The
/// bottom one never carries a label.
/// </summary>
public sealed class RuleChipView : View
{
    private readonly IUiDispatcher _dispatcher;
    private readonly GlyphSet _glyphs;
    private string? _label;

    public RuleChipView(TerminalGuiOptions options)
        : this(options, TerminalGuiDispatcher.Instance) { }

    internal RuleChipView(TerminalGuiOptions options, IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(options);
        _dispatcher = dispatcher;
        _glyphs = GlyphSet.Resolve(options.Glyphs, OutputIsUtf8());
        Height = 1;
        Width = Dim.Fill();
        CanFocus = false;
        SetScheme(SchemeFactory.Pane());
    }

    /// <summary>Names the recipient embedded at the right end; null or blank draws the bare rule.</summary>
    public void SetLabel(string? label)
    {
        _dispatcher.Invoke(() =>
        {
            _label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
            SetNeedsDraw();
        });
    }

    /// <summary>Repaints the rule (its width follows the layout).</summary>
    public void Refresh() => _dispatcher.Invoke(SetNeedsDraw);

    /// <inheritdoc />
    protected override bool OnDrawingContent(DrawContext? context)
    {
        SegmentPainter.Paint(this, 0, SeparatorModel.Compose(_glyphs, Viewport.Width, _label));
        return true;
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

    /// <summary>Test-only accessor for the embedded label ("" when the rule is bare).</summary>
    internal string CurrentLabel => _label ?? string.Empty;

    /// <summary>Test-only accessor for the composed row at a given width.</summary>
    internal string TextAt(int width) => RowSegment.Concat(SeparatorModel.Compose(_glyphs, width, _label));
}
