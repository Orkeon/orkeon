using Microsoft.Extensions.Logging;
using Orkeon.Cli.TerminalGui.Hosting;

namespace Orkeon.Cli.TerminalGui.Tests.Hosting;

public class TerminalGuiOptionsTests
{
    [Fact]
    public void Defaults_match_spec()
    {
        var o = new TerminalGuiOptions();
        Assert.Equal(0.5, o.InitialSplitRatio);
        Assert.Equal(LogLevel.Information, o.DefaultMinimumLogLevel);
        Assert.Equal(5000, o.LogsBufferCapacity);
        Assert.Equal("Logs", o.LogsPaneTitle);
        Assert.Equal("REPL", o.ReplPaneTitle);
    }

    [Fact]
    public void Record_with_expression_returns_new_instance_with_overridden_value()
    {
        var o = new TerminalGuiOptions { InitialSplitRatio = 0.7, LogsBufferCapacity = 1000 };
        Assert.Equal(0.7, o.InitialSplitRatio);
        Assert.Equal(1000, o.LogsBufferCapacity);
        Assert.Equal(LogLevel.Information, o.DefaultMinimumLogLevel);
    }
}
