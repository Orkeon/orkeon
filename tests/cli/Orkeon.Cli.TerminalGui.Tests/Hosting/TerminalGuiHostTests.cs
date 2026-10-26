using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Tests.Doubles;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Orkeon.Cli.TerminalGui.Tests.Layout;

namespace Orkeon.Cli.TerminalGui.Tests.Hosting;

// xUnit1004 suppressed: Skip is intentional. Terminal.Gui 2.1.0 ModuleInitializer crashes
// under xUnit (verified). Tracked: TUI-12.
#pragma warning disable xUnit1004 // Test methods should not be skipped

// Skip cause: Terminal.Gui 2.1.0 ModuleInitializer crashes inside xUnit test processes.
[Collection(TerminalGuiApplicationCollection.Name)]
public class TerminalGuiHostTests
{
    private const string SkipReason = "Terminal.Gui 2.1.0 module-init bug — see TUI-12";

    private static TerminalGuiHost CreateHost()
    {
        var options = new TerminalGuiOptions();
        return new TerminalGuiHost(options);
    }

    private const string Hint = "Ctrl+C or Ctrl+Shift+C copies a selection — esc esc interrupts the turn.";

    private static ReplPaneView CreatePane()
        => new(new TerminalGuiOptions(), InlineDispatcher.Instance);

    private static Key CtrlC() => new(KeyCode.C | KeyCode.CtrlMask);

    private static Key CtrlShiftC() => new(KeyCode.C | KeyCode.CtrlMask | KeyCode.ShiftMask);

    [Fact]
    public void CtrlC_copies_the_transcript_selection_when_the_input_has_none()
    {
        // The transcript is not focusable: its mouse selection is never the focused one, yet it is
        // what the user sees selected. Ctrl+C copies it instead of printing the hint.
        TuiClipboard.Reset();
        TuiClipboard.TerminalWriter = new StringWriter();
        try
        {
            var runner = new FakeInteractiveRunner { Running = false };
            using var pane = CreatePane();
            pane.AppendOutputLine("alpha beta");
            pane.HistoryView.SelectAll();

            TerminalGuiHost.HandleCtrlC(CtrlC(), runner, pane);

            Assert.True(TuiClipboard.TryGetText(out var copied));
            Assert.Contains("alpha beta", copied, StringComparison.Ordinal);
            Assert.Equal("alpha beta\n", pane.CurrentHistory);
        }
        finally
        {
            TuiClipboard.Reset();
            TuiClipboard.TerminalWriter = null;
        }
    }

    [Fact]
    public void CtrlShiftC_copies_the_transcript_selection_too()
    {
        // A terminal whose copy shortcut is Ctrl+Shift+C (Ptyxis, GNOME Terminal) passes it on
        // while the TUI holds the mouse: it reaches the same handler as Ctrl+C.
        TuiClipboard.Reset();
        TuiClipboard.TerminalWriter = new StringWriter();
        try
        {
            var runner = new FakeInteractiveRunner { Running = false };
            using var pane = CreatePane();
            pane.AppendOutputLine("gamma");
            pane.HistoryView.SelectAll();

            TerminalGuiHost.HandleCtrlC(CtrlShiftC(), runner, pane);

            Assert.True(TuiClipboard.TryGetText(out var copied));
            Assert.Contains("gamma", copied, StringComparison.Ordinal);
            Assert.Equal("gamma\n", pane.CurrentHistory);
        }
        finally
        {
            TuiClipboard.Reset();
            TuiClipboard.TerminalWriter = null;
        }
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public void CtrlC_without_selection_leaves_a_running_command_running()
    {
        TuiClipboard.Reset();
        var runner = new FakeInteractiveRunner { Running = true };
        using var pane = CreatePane();
        TerminalGuiHost.HandleCtrlC(CtrlC(), runner, pane);
        Assert.Equal(0, runner.CancelRequests);
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public void Two_CtrlC_within_two_seconds_do_not_request_stop()
    {
        TuiClipboard.Reset();
        var runner = new FakeInteractiveRunner { Running = true };
        using var pane = CreatePane();
        TerminalGuiHost.HandleCtrlC(CtrlC(), runner, pane);
        TerminalGuiHost.HandleCtrlC(CtrlC(), runner, pane);
        Assert.Equal(0, runner.CancelRequests);
        Assert.Equal(Hint + "\n" + Hint + "\n", pane.CurrentHistory);
    }

    [Fact]
    [Trait("BR", "CH-04")]
    public void CtrlC_without_selection_prints_the_interrupt_hint()
    {
        TuiClipboard.Reset();
        var runner = new FakeInteractiveRunner { Running = false };
        using var pane = CreatePane();
        TerminalGuiHost.HandleCtrlC(CtrlC(), runner, pane);
        Assert.Equal(Hint + "\n", pane.CurrentHistory);
    }

    private static Key CtrlV() => new(KeyCode.V | KeyCode.CtrlMask);

    [Fact]
    [Trait("BR", "CH-09")]
    public void CtrlV_pastes_the_clipboard_into_the_input()
    {
        TuiClipboard.Reset();
        TuiClipboard.TerminalWriter = new StringWriter();
        try
        {
            TuiClipboard.Copy("COLLÉ");
            using var pane = CreatePane();
            var key = CtrlV();
            var result = TerminalGuiHost.HandleCtrlV(key, pane);
            Assert.True(result);
            Assert.True(key.Handled);
            Assert.Equal("COLLÉ", pane.CurrentInput);
        }
        finally { TuiClipboard.Reset(); TuiClipboard.TerminalWriter = null; }
    }

    [Fact]
    [Trait("BR", "CH-09")]
    public void CtrlV_is_handled_before_the_focused_view()
    {
        TuiClipboard.Reset();
        TuiClipboard.TerminalWriter = new StringWriter();
        try
        {
            TuiClipboard.Copy("alpha");
            using var pane = CreatePane();
#pragma warning disable CS0618 // Terminal.Gui legacy static Application / obsolete TextView: plain focused view stand-in
            using var other = new Terminal.Gui.Views.TextView { Text = "other" };
#pragma warning restore CS0618
            var key = CtrlV();
            Assert.True(TerminalGuiHost.HandleCtrlV(key, pane, other));
            Assert.True(key.Handled);
            Assert.Equal("alpha", pane.CurrentInput);
            Assert.Equal("other", other.Text);
        }
        finally { TuiClipboard.Reset(); TuiClipboard.TerminalWriter = null; }
    }

    [Fact]
    [Trait("BR", "CH-09")]
    public void CtrlV_never_submits_the_input()
    {
        TuiClipboard.Reset();
        TuiClipboard.TerminalWriter = new StringWriter();
        try
        {
            TuiClipboard.Copy("line");
            using var pane = CreatePane();
            var read = pane.ReadLineAsync(CancellationToken.None);
            var key = CtrlV();
            Assert.True(TerminalGuiHost.HandleCtrlV(key, pane));
            Assert.Equal("line", pane.CurrentInput);
            Assert.False(read.IsCompleted);
        }
        finally { TuiClipboard.Reset(); TuiClipboard.TerminalWriter = null; }
    }

    [Fact]
    [Trait("BR", "CH-09")]
    public void CtrlV_stands_down_when_a_modal_dialog_has_focus()
    {
        TuiClipboard.Reset();
        TuiClipboard.TerminalWriter = new StringWriter();
        try
        {
            TuiClipboard.Copy("nope");
            using var pane = CreatePane();
#pragma warning disable CS0618 // Terminal.Gui legacy static Application / obsolete TextView: plain focused view stand-in
            using var plain = new Terminal.Gui.Views.TextView { Text = "plain" };
#pragma warning restore CS0618
            Assert.True(TerminalGuiHost.HandleCtrlV(CtrlV(), pane, plain));
            Assert.Equal("nope", pane.CurrentInput);
            pane.CurrentInput = "keep";
            using var dialog = new QuitConfirmDialog("quit?");
            var key = CtrlV();
            Assert.False(TerminalGuiHost.HandleCtrlV(key, pane, dialog));
            Assert.False(key.Handled);
            Assert.Equal("keep", pane.CurrentInput);
        }
        finally { TuiClipboard.Reset(); TuiClipboard.TerminalWriter = null; }
    }

    [Fact]
    [Trait("BR", "CH-09")]
    public void CtrlV_ignores_other_keys()
    {
        TuiClipboard.Reset();
        TuiClipboard.TerminalWriter = new StringWriter();
        try
        {
            TuiClipboard.Copy("nope");
            using var pane = CreatePane();
            Assert.True(TerminalGuiHost.HandleCtrlV(CtrlV(), pane));
            Assert.Equal("nope", pane.CurrentInput);
            pane.CurrentInput = "keep";
            var key = new Key(KeyCode.X | KeyCode.CtrlMask);
            Assert.False(TerminalGuiHost.HandleCtrlV(key, pane));
            Assert.False(key.Handled);
            Assert.Equal("keep", pane.CurrentInput);
        }
        finally { TuiClipboard.Reset(); TuiClipboard.TerminalWriter = null; }
    }

    [Fact]
    public async Task Toplevel_throws_before_initialize()
    {
        await using var host = CreateHost();
        Assert.Throws<InvalidOperationException>(() => host.Toplevel);
    }

    [Fact]
    public async Task Initialize_is_idempotent()
    {
        await using var host = CreateHost();
        host.Initialize();
        host.Initialize();
        Assert.NotNull(host.Toplevel);
    }

    [Fact]
    public async Task ShouldExposeIntegrationAndRefuseToplevel_WhenNotInitialized()
    {
        var integration = new TuiIntegration();
        await using var host = new TerminalGuiHost(new TerminalGuiOptions(), integration);

        Assert.Same(integration, host.Integration);
        Assert.Throws<InvalidOperationException>(() => host.Toplevel);
    }

    [Fact]
    public void ShouldThrow_WhenHostArgumentsAreNull()
    {
        Assert.Throws<ArgumentNullException>(() => new TerminalGuiHost(null!));
        Assert.Throws<ArgumentNullException>(() => new TerminalGuiHost(new TerminalGuiOptions(), null!));
    }
}
#pragma warning restore xUnit1004
