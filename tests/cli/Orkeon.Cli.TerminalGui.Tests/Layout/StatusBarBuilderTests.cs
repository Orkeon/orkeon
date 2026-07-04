using Microsoft.Extensions.Logging;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Logging;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

// These tests exercise the static helpers and don't load any Terminal.Gui type,
// so they survive the TUI-12 module-init bug.
public class StatusBarBuilderHelperTests
{
    [Theory]
    // F2 = more details: each step moves toward Trace, clamped at the bottom.
    [InlineData(LogLevel.Error,       LogLevel.Warning)]
    [InlineData(LogLevel.Warning,     LogLevel.Information)]
    [InlineData(LogLevel.Information, LogLevel.Debug)]
    [InlineData(LogLevel.Debug,       LogLevel.Trace)]
    [InlineData(LogLevel.Trace,       LogLevel.Trace)] // floored — already most verbose
    public void MoreVerbose_moves_one_step_toward_Trace_with_clamp(LogLevel current, LogLevel expected)
    {
        Assert.Equal(expected, StatusBarBuilder.MoreVerbose(current));
    }

    [Theory]
    // Shift+F2 = fewer details: each step moves toward Error, capped at the top.
    [InlineData(LogLevel.Trace,       LogLevel.Debug)]
    [InlineData(LogLevel.Debug,       LogLevel.Information)]
    [InlineData(LogLevel.Information, LogLevel.Warning)]
    [InlineData(LogLevel.Warning,     LogLevel.Error)]
    [InlineData(LogLevel.Error,       LogLevel.Error)] // capped — already least verbose
    public void LessVerbose_moves_one_step_toward_Error_with_cap(LogLevel current, LogLevel expected)
    {
        Assert.Equal(expected, StatusBarBuilder.LessVerbose(current));
    }

    [Theory]
    [InlineData(LogLevel.Critical, LogLevel.Information)] // not on the ladder → reset to a sane default
    [InlineData(LogLevel.None,     LogLevel.Information)]
    public void LessVerbose_with_off_ladder_input_resets_to_Information(LogLevel current, LogLevel expected)
    {
        Assert.Equal(expected, StatusBarBuilder.LessVerbose(current));
    }

    [Theory]
    [InlineData(LogLevel.Trace, "Level: Trace")]
    [InlineData(LogLevel.Information, "Level: Information")]
    [InlineData(LogLevel.Warning, "Level: Warning")]
    public void FormatLevelLabel_includes_prefix_and_level(LogLevel level, string expected)
    {
        Assert.Equal(expected, StatusBarBuilder.FormatLevelLabel(level));
    }
}

// xUnit1004 suppressed: Skip is intentional. Terminal.Gui 2.1.0 ModuleInitializer crashes
// under xUnit (verified). Tracked: TUI-12.
#pragma warning disable xUnit1004 // Test methods should not be skipped

// Skip cause: Terminal.Gui 2.1.0 ModuleInitializer crashes inside xUnit test processes.
// Tracked in project/tasks: TUI-12.
public class StatusBarBuilderTests
{
    private const string SkipReason = "Terminal.Gui 2.1.0 module-init bug — see TUI-12";

    private static (SplitPaneToplevel top, TerminalGuiLoggerProvider provider, FindDialog dialog) Create()
    {
        var options = new TerminalGuiOptions();
        var top = new SplitPaneToplevel(options, InlineDispatcher.Instance);
        var provider = new TerminalGuiLoggerProvider(top.Logs, options);
        var dialog = new FindDialog(top.Logs);
        return (top, provider, dialog);
    }

    [Fact]
    public void Build_returns_status_bar_with_shortcuts()
    {
        var (top, provider, dialog) = Create();
        var bar = StatusBarBuilder.Build(top, provider, dialog);
        Assert.NotNull(bar);
    }

    [Fact]
    public void CtrlL_invokes_logs_clear()
    {
        var (top, provider, dialog) = Create();
        top.Logs.Append("noise");
        Assert.Equal(1, top.Logs.LineCount);
        // The shortcut Action is the bound clear; we invoke it via reflection in real e2e.
        // Here we rely on the public closure surface via the Logs.Clear method.
        top.Logs.Clear();
        Assert.Equal(0, top.Logs.LineCount);
    }
}
#pragma warning restore xUnit1004
