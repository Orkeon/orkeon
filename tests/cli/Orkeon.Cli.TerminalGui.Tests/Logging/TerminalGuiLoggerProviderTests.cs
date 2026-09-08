using Microsoft.Extensions.Logging;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Logging;

namespace Orkeon.Cli.TerminalGui.Tests.Logging;

// xUnit1004 suppressed: Skip is intentional. Terminal.Gui 2.1.0 ModuleInitializer crashes
// under xUnit (verified). Tracked: TUI-12.
#pragma warning disable xUnit1004 // Test methods should not be skipped

// Skip cause: Terminal.Gui 2.1.0 ModuleInitializer crashes inside xUnit test processes.
public class TerminalGuiLoggerProviderTests
{
    private const string SkipReason = "Terminal.Gui 2.1.0 module-init bug — see TUI-12";

    private static (TerminalGuiLoggerProvider provider, LogsPaneView logs) Create(LogLevel min = LogLevel.Information)
    {
        var options = new TerminalGuiOptions { DefaultMinimumLogLevel = min };
        var logs = new LogsPaneView(options, InlineDispatcher.Instance);
        return (new TerminalGuiLoggerProvider(logs, options), logs);
    }

    [Fact]
    public void Append_below_minimum_level_is_dropped()
    {
        var (provider, logs) = Create(min: LogLevel.Warning);
        provider.CreateLogger("X").LogInformation("filtered");
        Assert.Equal(0, logs.LineCount);
    }

    [Fact]
    public void Append_at_or_above_minimum_level_is_written()
    {
        var (provider, logs) = Create(min: LogLevel.Warning);
        provider.CreateLogger("X").LogWarning("kept");
        Assert.Equal(1, logs.LineCount);
        Assert.Contains("kept", logs.CurrentText);
    }

    [Fact]
    public void SetMinimumLevel_changes_filter_at_runtime()
    {
        var (provider, logs) = Create(min: LogLevel.Warning);
        provider.CreateLogger("X").LogInformation("first-filtered");
        provider.SetMinimumLevel(LogLevel.Information);
        provider.CreateLogger("X").LogInformation("second-kept");
        Assert.Equal(1, logs.LineCount);
        Assert.Contains("second-kept", logs.CurrentText);
    }

    [Fact]
    public void CreateLogger_returns_same_instance_per_category()
    {
        var (provider, _) = Create();
        var a = provider.CreateLogger("Cat");
        var b = provider.CreateLogger("Cat");
        Assert.Same(a, b);
    }

    [Fact]
    public void Logger_writes_format_to_pane()
    {
        var (provider, logs) = Create();
        provider.CreateLogger("Orkeon.Foo.Bar").LogInformation("hello");
        Assert.Contains("[INF] Bar: hello", logs.CurrentText);
    }
}
#pragma warning restore xUnit1004
