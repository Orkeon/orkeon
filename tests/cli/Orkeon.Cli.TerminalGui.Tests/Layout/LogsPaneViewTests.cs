using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

// xUnit1004 suppressed: Skip is intentional. Terminal.Gui 2.1.0 ModuleInitializer crashes
// under xUnit (verified). Tracked: TUI-12.
#pragma warning disable xUnit1004 // Test methods should not be skipped

// Skip cause: Terminal.Gui 2.1.0 ModuleInitializer crashes inside xUnit test processes
// (TypeLoadException on MemberNotNullWhenAttribute via Microsoft.TestPlatform.CoreUtilities).
// Tracked in project/tasks: TUI-12.
public class LogsPaneViewTests
{
    private const string SkipReason = "Terminal.Gui 2.1.0 module-init bug — see TUI-12";

    private static LogsPaneView CreatePane(int capacity = 100)
        => new(new TerminalGuiOptions { LogsBufferCapacity = capacity }, InlineDispatcher.Instance);

    [Fact]
    public void Append_increments_line_count()
    {
        using var pane = CreatePane();
        pane.Append("first");
        pane.Append("second");
        Assert.Equal(2, pane.LineCount);
    }

    [Fact]
    public void Append_above_capacity_drops_oldest()
    {
        using var pane = CreatePane(capacity: 3);
        pane.Append("a");
        pane.Append("b");
        pane.Append("c");
        pane.Append("d");
        Assert.Equal(3, pane.LineCount);
        Assert.Equal("b\nc\nd", pane.CurrentText);
    }

    [Fact]
    public void Clear_resets_line_count_to_zero()
    {
        using var pane = CreatePane();
        pane.Append("x");
        pane.Append("y");
        pane.Clear();
        Assert.Equal(0, pane.LineCount);
        Assert.Equal(string.Empty, pane.CurrentText);
    }

    [Fact]
    public void Append_is_thread_safe()
    {
        using var pane = CreatePane(capacity: 10_000);
        const int threads = 8, perThread = 500;
        Parallel.For(0, threads, t =>
        {
            for (int i = 0; i < perThread; i++)
                pane.Append($"t{t}-i{i}");
        });
        Assert.Equal(threads * perThread, pane.LineCount);
    }
}
#pragma warning restore xUnit1004
