using System.Text;
using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Logging;

namespace Orkeon.Cli.TerminalGui.Tests.Logging;

public class LogsPaneTextWriterTests
{
    private static LogsPaneView CreatePane()
        => new(new TerminalGuiOptions { LogsBufferCapacity = 100 }, InlineDispatcher.Instance);

    [Fact]
    public void ShouldThrow_WhenPaneIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new LogsPaneTextWriter(null!));
    }

    [Fact]
    public void ShouldReportUtf8Encoding()
    {
        using var pane = CreatePane();
        using var writer = new LogsPaneTextWriter(pane);

        Assert.Equal(Encoding.UTF8, writer.Encoding);
    }

    [Fact]
    public void ShouldForwardCompleteLinesOnly_WhenCharsAreWritten()
    {
        using var pane = CreatePane();
        using var writer = new LogsPaneTextWriter(pane);

        writer.Write('a');
        writer.Write('b');
        Assert.Equal(0, pane.LineCount);
        writer.Write('\r');
        writer.Write('\n');

        Assert.Equal(1, pane.LineCount);
        Assert.Equal("ab", pane.CurrentText);
    }

    [Fact]
    public void ShouldSplitOnNewlinesAndStripCarriageReturns_WhenStringIsWritten()
    {
        using var pane = CreatePane();
        using var writer = new LogsPaneTextWriter(pane);

        writer.Write("one\r\ntwo\nthr");
        writer.Write((string?)null);
        writer.Write(string.Empty);
        writer.Write("ee\n");

        Assert.Equal(3, pane.LineCount);
        Assert.Equal("one\ntwo\nthree", pane.CurrentText);
    }

    [Fact]
    public void ShouldFlushPendingText_WhenWriteLineIsCalled()
    {
        using var pane = CreatePane();
        using var writer = new LogsPaneTextWriter(pane);

        writer.Write("partial");
        writer.WriteLine();
        writer.WriteLine("full");
        writer.WriteLine((string?)null);

        Assert.Equal(3, pane.LineCount);
        Assert.Equal("partial\nfull\n", pane.CurrentText);
    }

    [Fact]
    public void ShouldEmitEmptyLine_WhenWriteLineHasNothingPending()
    {
        using var pane = CreatePane();
        using var writer = new LogsPaneTextWriter(pane);

        writer.WriteLine();

        Assert.Equal(1, pane.LineCount);
    }
}
