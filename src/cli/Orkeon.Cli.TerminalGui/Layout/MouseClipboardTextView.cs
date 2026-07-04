using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Orkeon.Cli.TerminalGui.Layout;

/// <summary>
/// <see cref="TextView"/> that:
/// <list type="bullet">
///   <item>Auto-copies the current selection to the OS clipboard on left-button release.</item>
///   <item>Renders the selection in inverse video (foreground/background swap) so the
///   user gets the standard terminal-style visual feedback during drag-select.</item>
/// </list>
/// The native v2 drag-select logic is preserved (we call <c>base.OnMouseEvent</c> first);
/// we only add the auto-copy hook and override the selection color.
/// </summary>
/// <remarks>
/// <para>
/// Clipboard target = OS clipboard (xclip on Linux, PowerShell on WSL, pbcopy on macOS,
/// native API on Windows). On hosts where no integration is available,
/// <c>TrySetClipboardData</c> returns false silently.
/// </para>
/// <para>
/// Selection color: <see cref="TextView.OnDrawSelectionColor"/> default uses
/// <see cref="Scheme.Focus"/> which in <c>SchemeFactory.Pane()</c> is
/// BrightCyan-on-Black — visually weak. We override to use an explicit black-on-white
/// inverse-video attribute that is unmistakable.
/// </para>
/// </remarks>
public sealed class MouseClipboardTextView : TextView
{
    private static readonly Color SelectionFg = new(ColorName16.Black);
    private static readonly Color SelectionBg = new(ColorName16.White);
    private static readonly Attribute SelectionAttribute = new(in SelectionFg, in SelectionBg);

    /// <summary>
    /// Keyboard clipboard support for the active (focused) pane. These views are read-only, so only
    /// Ctrl+A (select all) and Ctrl+C (copy selection → OS clipboard) apply; paste is handled on the
    /// editable REPL input. Unhandled keys fall through to the native <see cref="TextView"/> bindings.
    /// </summary>
    /// <remarks>
    /// In runner mode Ctrl+C is intercepted earlier by the host's global handler
    /// (<see cref="Orkeon.Cli.TerminalGui.Hosting.TerminalGuiHost"/>), which copies the focused
    /// selection before falling back to cancel-command. This override keeps the view self-contained
    /// for the non-runner / direct-focus paths.
    /// </remarks>
    protected override bool OnKeyDown(Key key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (PaneClipboard.IsCtrl(key, KeyCode.A))
        {
            SelectAll();
            return true;
        }

        if (PaneClipboard.IsCtrl(key, KeyCode.C) && PaneClipboard.CopySelection(this))
            return true;

        return base.OnKeyDown(key);
    }

    /// <inheritdoc />
    protected override bool OnMouseEvent(Mouse mouse)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        var handled = base.OnMouseEvent(mouse);

        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased)
            || mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
        {
            var selected = SelectedText;
            var clipboard = Application.Clipboard;
            if (clipboard is not null && !string.IsNullOrEmpty(selected))
            {
                clipboard.TrySetClipboardData(selected);
            }
        }

        return handled;
    }

    /// <inheritdoc />
    protected override void OnDrawSelectionColor(List<Cell> line, int idxCol, int idxRow)
    {
        SetAttribute(SelectionAttribute);
    }
}
