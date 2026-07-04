using Microsoft.Extensions.Logging;
using Orkeon.Cli.TerminalGui.Logging;

namespace Orkeon.Cli.TerminalGui.Tests.Logging;

// These tests exercise the static formatting helpers and don't load any Terminal.Gui type,
// so they survive the TUI-12 module-init bug.
public class TerminalGuiLoggerProviderFormatTests
{
    [Fact]
    public void Format_includes_timestamp_level_category_message()
    {
        var ts = new DateTimeOffset(2026, 5, 9, 14, 23, 45, TimeSpan.Zero);
        var entry = new LogEntry(ts, LogLevel.Information, "Orkeon.Foo.Bar", "hello world", null);
        var line = TerminalGuiLoggerProvider.Format(entry);
        Assert.Contains("[INF]", line);
        Assert.Contains("Bar:", line);
        Assert.Contains("hello world", line);
        Assert.Contains("14:23:45", line);
    }

    [Fact]
    public void Format_appends_exception_details_when_present()
    {
        var ex = new InvalidOperationException("boom");
        var entry = new LogEntry(DateTimeOffset.Now, LogLevel.Error, "Cat", "failed", ex);
        var line = TerminalGuiLoggerProvider.Format(entry);
        Assert.Contains("[ERR]", line);
        Assert.Contains("InvalidOperationException", line);
        Assert.Contains("boom", line);
    }

    [Theory]
    [InlineData(LogLevel.Trace, "TRC")]
    [InlineData(LogLevel.Debug, "DBG")]
    [InlineData(LogLevel.Information, "INF")]
    [InlineData(LogLevel.Warning, "WRN")]
    [InlineData(LogLevel.Error, "ERR")]
    [InlineData(LogLevel.Critical, "CRT")]
    public void Format_maps_each_log_level_to_its_short_tag(LogLevel level, string expectedTag)
    {
        var entry = new LogEntry(DateTimeOffset.UnixEpoch, level, "Cat", "m", null);
        Assert.Contains($"[{expectedTag}]", TerminalGuiLoggerProvider.Format(entry));
    }

    [Fact]
    public void ShortenCategory_keeps_last_segment()
    {
        Assert.Equal("Bar", TerminalGuiLoggerProvider.ShortenCategory("Orkeon.Foo.Bar"));
        Assert.Equal("Bare", TerminalGuiLoggerProvider.ShortenCategory("Bare"));
    }
}
