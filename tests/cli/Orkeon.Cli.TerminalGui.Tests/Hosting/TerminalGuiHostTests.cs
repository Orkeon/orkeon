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

    private const string Hint = "Ctrl+C copies a selection — esc esc interrupts the turn.";

    private static ReplPaneView CreatePane()
        => new(new TerminalGuiOptions(), InlineDispatcher.Instance);

    private static Key CtrlC() => new(KeyCode.C | KeyCode.CtrlMask);

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
}
#pragma warning restore xUnit1004
