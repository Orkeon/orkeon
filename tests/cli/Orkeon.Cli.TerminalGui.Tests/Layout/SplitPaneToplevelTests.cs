using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

// xUnit1004 suppressed: Skip is intentional. Terminal.Gui 2.1.0 ModuleInitializer crashes
// under xUnit (verified). Tracked: TUI-12.
#pragma warning disable xUnit1004 // Test methods should not be skipped

// Skip cause: Terminal.Gui 2.1.0 ModuleInitializer crashes inside xUnit test processes.
// Tracked in project/tasks: TUI-12.
public class SplitPaneToplevelTests
{
    private const string SkipReason = "Terminal.Gui 2.1.0 module-init bug — see TUI-12";

    private static SplitPaneToplevel Create(double initialRatio = 0.5)
        => new(new TerminalGuiOptions { InitialSplitRatio = initialRatio }, InlineDispatcher.Instance);

    [Fact]
    public void Construction_creates_the_fidelity_panes_with_logs_hidden()
    {
        using var top = Create();
        Assert.NotNull(top.Logs);
        Assert.NotNull(top.Repl);
        Assert.NotNull(top.HintBar);
        Assert.NotNull(top.Agents);
        Assert.Equal(0.5, top.CurrentSplitRatio);
        // The logs drawer starts HIDDEN (PLAN phase 1) — it is our addition, not the
        // reference's — and the REPL is the surface that must always be there.
        Assert.False(top.IsLogsVisible);
        Assert.True(top.IsReplVisible);
    }

    [Fact]
    public void Logs_drawer_opens_at_startup_when_the_option_says_so()
    {
        using var top = new SplitPaneToplevel(
            new TerminalGuiOptions { LogsVisibleAtStartup = true }, InlineDispatcher.Instance);
        Assert.True(top.IsLogsVisible);
    }

    [Fact]
    public void ToggleLogsVisible_flips_the_drawer()
    {
        using var top = Create();
        top.ToggleLogsVisible();
        Assert.True(top.IsLogsVisible);
        top.ToggleLogsVisible();
        Assert.False(top.IsLogsVisible);
    }

    [Fact]
    public void ToggleReplVisible_refused_when_logs_hidden()
    {
        using var top = Create();
        // Logs start hidden: hiding the REPL too would leave only the hint bar.
        Assert.False(top.IsLogsVisible);
        top.ToggleReplVisible();
        Assert.True(top.IsReplVisible);
    }

    [Fact]
    public void SetSplitRatio_clamps_below_minimum()
    {
        using var top = Create();
        top.SetSplitRatio(0.05);
        Assert.Equal(0.1, top.CurrentSplitRatio);
    }

    [Fact]
    public void SetSplitRatio_clamps_above_maximum()
    {
        using var top = Create();
        top.SetSplitRatio(0.99);
        Assert.Equal(0.9, top.CurrentSplitRatio);
    }

    [Fact]
    public void SetSplitRatio_within_range_is_applied()
    {
        using var top = Create();
        top.SetSplitRatio(0.7);
        Assert.Equal(0.7, top.CurrentSplitRatio);
    }
}
#pragma warning restore xUnit1004
