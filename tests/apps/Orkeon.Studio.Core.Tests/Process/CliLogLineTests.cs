using Orkeon.Studio.Core.Process;

namespace Orkeon.Studio.Core.Tests.Process;

/// <summary>
/// A console log line of the CLI, as the Run screen's journal reads it: under <c>--events</c> every
/// log goes to stderr, so the stream says nothing about severity — the level token does.
/// </summary>
public sealed class CliLogLineTests
{
    [Theory]
    [InlineData("trce", CliLogLevel.Trace)]
    [InlineData("dbug", CliLogLevel.Debug)]
    [InlineData("info", CliLogLevel.Information)]
    [InlineData("warn", CliLogLevel.Warning)]
    [InlineData("fail", CliLogLevel.Error)]
    [InlineData("crit", CliLogLevel.Critical)]
    public void Each_console_level_token_is_read(string token, CliLogLevel expected)
    {
        var line = CliLogLine.Parse($"13:55:05.057 {token}: Orkeon.Verbose[0] Agent loop started");

        Assert.Equal(expected, line.Level);
    }

    [Fact]
    public void The_logger_timestamp_is_dropped_and_the_rest_is_kept()
    {
        var line = CliLogLine.Parse("13:55:05.057 info: Orkeon.Verbose[0] Agent loop started");

        Assert.Equal("info: Orkeon.Verbose[0] Agent loop started", line.Text);
    }

    [Fact]
    public void A_line_without_timestamp_keeps_its_level()
    {
        var line = CliLogLine.Parse("warn: Orkeon.Hosting[3] No default model");

        Assert.Equal(CliLogLevel.Warning, line.Level);
        Assert.Equal("warn: Orkeon.Hosting[3] No default model", line.Text);
    }

    [Theory]
    [InlineData("=== Crew Output ===")]
    [InlineData("Unhandled exception. System.InvalidOperationException: boom")]
    [InlineData("the agent said fail: nothing")]
    [InlineData("")]
    public void A_line_that_is_not_a_log_line_has_no_level_and_stays_as_is(string text)
    {
        var line = CliLogLine.Parse(text);

        Assert.Null(line.Level);
        Assert.Equal(text, line.Text);
    }

    [Fact]
    public void A_timestamp_alone_is_not_stripped_from_an_ordinary_line()
    {
        var line = CliLogLine.Parse("13:55:05.057 the clock says hello");

        Assert.Null(line.Level);
        Assert.Equal("13:55:05.057 the clock says hello", line.Text);
    }
}
