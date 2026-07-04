using Orkeon.Cli.TerminalGui.Layout;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

// Pure formatter tests — target TasksPaneFormatter so they don't load any
// Terminal.Gui-derived type (and trip the TUI-12 module-init bug).
public class TasksPaneFormatterTests
{
    [Theory]
    [InlineData(0, 0, "0:00")]
    [InlineData(0, 5, "0:05")]
    [InlineData(0, 42, "0:42")]
    [InlineData(1, 7, "1:07")]
    [InlineData(12, 30, "12:30")]
    public void FormatElapsed_uses_minutes_seconds_under_one_hour(int minutes, int seconds, string expected)
    {
        Assert.Equal(expected, TasksPaneFormatter.FormatElapsed(new TimeSpan(0, minutes, seconds)));
    }

    [Theory]
    [InlineData(1, 0, 0, "1:00:00")]
    [InlineData(2, 5, 7, "2:05:07")]
    [InlineData(10, 30, 59, "10:30:59")]
    public void FormatElapsed_uses_hours_minutes_seconds_above_one_hour(int hours, int minutes, int seconds, string expected)
    {
        Assert.Equal(expected, TasksPaneFormatter.FormatElapsed(new TimeSpan(hours, minutes, seconds)));
    }
}
