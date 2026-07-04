using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

// Run live under Terminal.Gui 2.4.4 (same conditions as ReplPaneViewTests). These exercise the
// deterministic, OS-clipboard-independent parts of the keyboard clipboard helper: key matching,
// select-all, caret insertion and the no-selection / read-only guards. The actual OS clipboard
// write/read paths mirror the (untested by convention) MouseClipboard* helpers and are not asserted
// here because xclip/pbcopy are absent on headless CI.
public class PaneClipboardTests
{
    [Fact]
    public void BareKey_strips_ctrl_modifier()
    {
        Assert.Equal(KeyCode.A, PaneClipboard.BareKey(Key.A.WithCtrl));
    }

    [Fact]
    public void IsCtrl_matches_ctrl_letter()
    {
        Assert.True(PaneClipboard.IsCtrl(Key.C.WithCtrl, KeyCode.C));
        Assert.False(PaneClipboard.IsCtrl(Key.C.WithCtrl, KeyCode.V));
        Assert.False(PaneClipboard.IsCtrl(new Key(KeyCode.C), KeyCode.C)); // no Ctrl
    }

    [Fact]
    public void SelectAll_selects_textfield_content()
    {
        using var field = new TextField { Text = "hello world" };
        Assert.True(PaneClipboard.SelectAll(field));
        Assert.Equal("hello world", field.SelectedText);
    }

    [Fact]
    public void SelectAll_selects_textview_content()
    {
        using var view = new MouseClipboardTextView { Text = "line one\nline two" };
        Assert.True(PaneClipboard.SelectAll(view));
        Assert.Equal("line one\nline two", view.SelectedText.Replace("\r\n", "\n"));
    }

    [Fact]
    public void SelectAll_is_noop_for_unsupported_view()
    {
        using var label = new Label();
        Assert.False(PaneClipboard.SelectAll(label));
        Assert.False(PaneClipboard.SelectAll(null));
    }

    [Fact]
    public void CopySelection_returns_false_when_nothing_selected()
    {
        // Text present but no selection ⇒ caller treats Ctrl+C as "cancel command", not "copy".
        using var field = new TextField { Text = "not selected" };
        Assert.False(PaneClipboard.CopySelection(field));
    }

    [Fact]
    public void PasteInto_is_noop_for_readonly_input()
    {
        // Read-only guard returns before touching the OS clipboard ⇒ deterministic on headless CI.
        using var input = new MouseClipboardTextView { Text = "x", ReadOnly = true };
        Assert.False(PaneClipboard.PasteInto(input));
        Assert.Equal("x", input.Text);
    }
}
