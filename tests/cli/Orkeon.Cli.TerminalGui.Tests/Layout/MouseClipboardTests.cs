using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

/// <summary>
/// Pinned behaviour of <see cref="MouseClipboardBehavior"/> (right-click paste into the REPL input)
/// and <see cref="MouseClipboardTextView"/> (release of a drag copies the selection).
/// </summary>
[Collection(TerminalGuiApplicationCollection.Name)]
public sealed class MouseClipboardTests : IDisposable
{
    public MouseClipboardTests() => TuiClipboard.Reset();

    public void Dispose()
    {
        TuiClipboard.Reset();
        TuiClipboard.TerminalWriter = null;
        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("BR", "CH-09")]
    public void Right_click_pastes_the_clipboard_into_the_input()
    {
        TuiClipboard.TerminalWriter = new StringWriter();
        TuiClipboard.Copy("COLLÉ");
        using var input = new MouseClipboardTextView { Text = "", ReadOnly = false };
        using var subscription = MouseClipboardBehavior.AttachRightClickPaste(() => input);

#pragma warning disable CS0618 // Terminal.Gui legacy static Application / obsolete TextView: the behaviour subscribes to the static mouse
        Application.Mouse.RaiseMouseEvent(new Mouse { Flags = MouseFlags.RightButtonClicked });
#pragma warning restore CS0618

        Assert.Equal("COLLÉ", input.Text);
    }

    [Fact]
    [Trait("BR", "CH-10")]
    public void Releasing_a_drag_in_the_transcript_copies_the_selection()
    {
        TuiClipboard.TerminalWriter = new StringWriter();
        using var transcript = new MouseClipboardTextView { Text = "alpha beta", ReadOnly = true, Width = 20, Height = 3 };
        transcript.SetRelativeLayout(new System.Drawing.Size(20, 3));
        transcript.SelectAll();

        transcript.NewMouseEvent(new Mouse { Flags = MouseFlags.LeftButtonReleased, Position = new System.Drawing.Point(0, 0) });

        Assert.True(TuiClipboard.TryGetText(out var text));
        Assert.Equal("alpha beta", text);
    }
}
