using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.App;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

[Collection(TerminalGuiApplicationCollection.Name)]
public class TranscriptViewTests
{
    private static TranscriptView CreateView(int capacity = 100, int width = 20, int height = 5)
    {
        var view = new TranscriptView(capacity);
        view.Frame = new System.Drawing.Rectangle(0, 0, width, height);
        view.Layout();
        return view;
    }

    [Fact]
    public void ShouldSelectTheSourceText_WhenSelectAllIsCalledOnWrappedLines()
    {
        using var view = CreateView(width: 8);
        view.AppendLines(["a long line that wraps", "end"]);

        Assert.True(PaneClipboard.SelectAll(view));

        Assert.Equal("a long line that wraps\nend", view.SelectedText);
    }

    [Fact]
    public void ShouldScrollToTheLastRow_WhenMoveEndIsCalled()
    {
        using var view = CreateView(height: 5);
        view.AppendLines(Enumerable.Range(0, 50).Select(i => $"row {i}"));

        view.MoveEnd();

        Assert.Equal(45, view.Viewport.Y);
    }

    [Fact]
    public void ShouldKeepTheText_WhenTheWrapIsToggled()
    {
        using var view = CreateView(width: 6);
        view.Append("one two three\n");
        var wrappedRows = view.Buffer.RowCount;

        view.WordWrap = false;

        Assert.True(wrappedRows > view.Buffer.RowCount);
        Assert.Equal("one two three\n", view.TranscriptText);
    }

#pragma warning disable CS0618 // Terminal.Gui legacy static Application: the headless ansi driver is reached through it
    [Fact]
    public void ShouldDrawTheTailOfBothPanes_WhenTheToplevelRendersHeadless()
    {
        Application.Init(driverName: "ansi");
        try
        {
            var options = new TerminalGuiOptions { LogsVisibleAtStartup = true };
            using var top = new SplitPaneToplevel(options, InlineDispatcher.Instance);
            Application.Begin(top);
            for (var i = 0; i < 500; i++) top.Repl.AppendOutputLine($"transcript {i}");
            for (var i = 0; i < 500; i++) top.Logs.Append($"log {i}");

            Application.LayoutAndDraw(true);

            var screen = Application.Driver!.ToString() ?? string.Empty;
            Assert.Contains("transcript 499", screen, StringComparison.Ordinal);
            Assert.Contains("log 499", screen, StringComparison.Ordinal);
            Assert.DoesNotContain("transcript 0\n", screen, StringComparison.Ordinal);
        }
        finally
        {
            Application.Shutdown();
        }
    }
#pragma warning restore CS0618
}
