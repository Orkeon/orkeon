using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// Paints an <see cref="InlinePanelModel"/> in place of the prompt rows and hands the keys it gets to the pane, which
/// owns what they mean. Holds no state of its own beyond the model it is showing.
/// </summary>
internal sealed class InlinePanelView : View
{
    public InlinePanelView()
    {
        X = 0;
        Width = Dim.Fill();
        CanFocus = true;
        Visible = false;
        SetScheme(SchemeFactory.Pane());
    }

    /// <summary>The panel shown; null while none is open.</summary>
    public InlinePanelModel? Model { get; set; }

    /// <summary>What the pane does with a key; true when it took it.</summary>
    public Func<Key, bool>? KeyHandler { get; set; }

    /// <inheritdoc />
    protected override bool OnKeyDown(Key key)
        => KeyHandler?.Invoke(key) == true || base.OnKeyDown(key);

    /// <inheritdoc />
    protected override bool OnDrawingContent(DrawContext? context)
    {
        var rows = Model?.Rows(Viewport.Width, Viewport.Height) ?? [];
        for (var y = 0; y < Viewport.Height; y++)
            SegmentPainter.Paint(this, y, y < rows.Count ? rows[y] : []);
        return true;
    }
}
