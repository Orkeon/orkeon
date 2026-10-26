using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.App;
using Terminal.Gui.Input;

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

    // The view sits ten rows below the screen's top unless a test says otherwise.
    private static Mouse Drag(int x, int y, bool pressed = true, int screenTop = 10) => new()
    {
        Flags = pressed ? MouseFlags.LeftButtonPressed | MouseFlags.PositionReport : MouseFlags.LeftButtonReleased,
        Position = new System.Drawing.Point(x, y),
        ScreenPosition = new System.Drawing.Point(x, screenTop + y),
    };

    [Fact]
    public void ShouldScrollUp_WhenADragIsHeldOnTheFirstRowOfAViewAtTheScreenTop()
    {
        // The REPL pane starts at the screen's first row: no mouse report can come from above the
        // transcript, so the drag held on its first row scrolls up.
        using var view = CreateView(height: 5);
        view.AppendLines(Enumerable.Range(0, 50).Select(i => $"row {i}"));
        view.MoveEnd();

        view.NewMouseEvent(Drag(0, 2, screenTop: 0));
        view.NewMouseEvent(Drag(0, 0, screenTop: 0));

        Assert.Equal(44, view.Viewport.Y);
        Assert.Equal("row 44\nrow 45\nrow 46\n", view.SelectedText);
    }

    [Fact]
    public void ShouldNotScroll_WhenASelectionStartsOnTheFirstRowOfAViewAtTheScreenTop()
    {
        using var view = CreateView(height: 5);
        view.AppendLines(Enumerable.Range(0, 50).Select(i => $"row {i}"));
        view.MoveEnd();

        view.NewMouseEvent(Drag(0, 0, screenTop: 0));
        view.NewMouseEvent(Drag(2, 0, screenTop: 0));
        view.NewMouseEvent(Drag(2, 0, pressed: false, screenTop: 0));

        Assert.Equal(45, view.Viewport.Y);
        Assert.Equal("ro", view.SelectedText);
    }

    [Fact]
    public void ShouldScrollUpAndExtendTheSelection_WhenADragIsHeldAboveTheView()
    {
        using var view = CreateView(height: 5);
        view.AppendLines(Enumerable.Range(0, 50).Select(i => $"row {i}"));
        view.MoveEnd();
        Assert.Equal(45, view.Viewport.Y);

        view.NewMouseEvent(Drag(0, 2));
        view.NewMouseEvent(Drag(0, -1));

        // One row up per report; the selection runs from the new top row to the anchor.
        Assert.Equal(44, view.Viewport.Y);
        Assert.Equal("row 44\nrow 45\nrow 46\n", view.SelectedText);

        view.NewMouseEvent(Drag(0, -1));
        Assert.Equal(43, view.Viewport.Y);
        Assert.Equal("row 43\nrow 44\nrow 45\nrow 46\n", view.SelectedText);
    }

    [Fact]
    public void ShouldScrollDownAndExtendTheSelection_WhenADragIsHeldBelowTheView()
    {
        using var view = CreateView(height: 5);
        view.AppendLines(Enumerable.Range(0, 50).Select(i => $"row {i}"));
        view.Viewport = view.Viewport with { Y = 0 };

        view.NewMouseEvent(Drag(0, 1));
        view.NewMouseEvent(Drag(0, 5));

        Assert.Equal(1, view.Viewport.Y);
        Assert.Equal("row 1\nrow 2\nrow 3\nrow 4\n", view.SelectedText);
    }

    [Fact]
    public void ShouldKeepTheScrolledSelectionAndCopyIt_WhenTheDragIsReleasedPastTheEdge()
    {
        TuiClipboard.Reset();
        TuiClipboard.TerminalWriter = new StringWriter();
        try
        {
            using var view = CreateView(height: 5);
            view.AppendLines(Enumerable.Range(0, 50).Select(i => $"row {i}"));
            view.Viewport = view.Viewport with { Y = 1 };

            view.NewMouseEvent(Drag(0, 3));
            view.NewMouseEvent(Drag(0, -1));
            // At the buffer's first row the selection reaches its start, as before the scroll existed.
            view.NewMouseEvent(Drag(0, -1));
            view.NewMouseEvent(Drag(0, -1, pressed: false));

            Assert.Equal(0, view.Viewport.Y);
            Assert.Equal("row 0\nrow 1\nrow 2\nrow 3\n", view.SelectedText);
            Assert.True(TuiClipboard.TryGetText(out var copied));
            Assert.Equal("row 0\nrow 1\nrow 2\nrow 3\n", copied);
        }
        finally
        {
            TuiClipboard.Reset();
            TuiClipboard.TerminalWriter = null;
        }
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

    [Fact]
    public void ShouldFollowTheTailAgain_WhenAHeightChangeBringsTheViewportToTheEnd()
    {
        using var view = CreateView(height: 5);
        view.AppendLines(Enumerable.Range(0, 50).Select(i => $"row {i}"));
        view.MoveEnd();
        view.Viewport = view.Viewport with { Y = 40 };

        // Ten rows high, row 40 is the last viewport: the view is at its end without a scroll.
        view.Frame = new System.Drawing.Rectangle(0, 0, 20, 10);
        view.Layout();
        Assert.Equal(40, view.Viewport.Y);
        view.AppendLines(["row 50"]);

        Assert.Equal(41, view.Viewport.Y);
    }

    [Fact]
    public void ShouldKeepTheLogsWhereTheUserScrolled_WhenEntriesArrive()
    {
        var options = new TerminalGuiOptions { LogsVisibleAtStartup = true };
        using var logs = new LogsPaneView(options, InlineDispatcher.Instance);
        var view = logs.Transcript;
        view.Frame = new System.Drawing.Rectangle(0, 0, 20, 5);
        view.Layout();
        for (var i = 0; i < 50; i++) logs.Append($"log {i}");
        Assert.Equal(45, view.Viewport.Y);
        view.Viewport = view.Viewport with { Y = 10 };

        logs.Append("log 50");
        Assert.Equal(10, view.Viewport.Y);

        view.MoveEnd();
        logs.Append("log 51");
        Assert.Equal(47, view.Viewport.Y);
    }

    [Fact]
    public void ShouldKeepTheTranscriptWhereTheUserScrolled_WhenOutputArrives()
    {
        using var repl = new ReplPaneView(new TerminalGuiOptions(), InlineDispatcher.Instance);
        var view = repl.HistoryView;
        view.Frame = new System.Drawing.Rectangle(0, 0, 20, 5);
        view.Layout();
        for (var i = 0; i < 50; i++) repl.AppendOutputLine($"line {i}");
        var end = view.Viewport.Y;
        Assert.True(end > 10);
        view.Viewport = view.Viewport with { Y = 10 };

        repl.AppendOutput("token");
        Assert.Equal(10, view.Viewport.Y);

        view.MoveEnd();
        repl.AppendOutputLine(" more");
        Assert.Equal(end + 1, view.Viewport.Y);
    }

#pragma warning disable CS0618 // Terminal.Gui legacy static Application: the headless ansi driver is reached through it
    [Fact]
    public void ShouldDrawTheTextAfterAnOverflowingTab_WhenTheTabStopsShortOfTheRowEnd()
    {
        Application.Init(driverName: "ansi");
        try
        {
            using var top = new Terminal.Gui.Views.Window();
            using var view = new TranscriptView(10) { Width = 6, Height = 3 };
            top.Add(view);
            Application.Begin(top);
            view.AppendLines(["abcde\tZ"]);

            Application.LayoutAndDraw(true);

            var screen = Application.Driver!.ToString() ?? string.Empty;
            Assert.Contains("abcde", screen, StringComparison.Ordinal);
            Assert.Contains("Z", screen, StringComparison.Ordinal);
        }
        finally
        {
            Application.Shutdown();
        }
    }

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

    [Fact]
    public void ShouldDrawTheTailOfTheLogs_WhenTheyAreShownAfterStartingHidden()
    {
        Application.Init(driverName: "ansi");
        try
        {
            var options = new TerminalGuiOptions { LogsVisibleAtStartup = false };
            using var top = new SplitPaneToplevel(options, InlineDispatcher.Instance);
            Application.Begin(top);
            Application.LayoutAndDraw(true);
            for (var i = 0; i < 500; i++) top.Logs.Append($"log {i}");

            top.ToggleLogsVisible();
            Application.LayoutAndDraw(true);

            var screen = Application.Driver!.ToString() ?? string.Empty;
            Assert.Contains("log 499", screen, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"\blog 0\b", screen);

            // A second show, after more entries arrived while hidden, also lands on the tail.
            top.ToggleLogsVisible();
            Application.LayoutAndDraw(true);
            for (var i = 0; i < 300; i++) top.Logs.Append($"more {i}");
            top.ToggleLogsVisible();
            Application.LayoutAndDraw(true);

            screen = Application.Driver!.ToString() ?? string.Empty;
            Assert.Contains("more 299", screen, StringComparison.Ordinal);
        }
        finally
        {
            Application.Shutdown();
        }
    }

    [Fact]
    public void ShouldScrollTheLogs_WhenNavigationKeysArePressedInTheFocusedPane()
    {
        Application.Init(driverName: "ansi");
        try
        {
            var options = new TerminalGuiOptions { LogsVisibleAtStartup = true };
            using var top = new SplitPaneToplevel(options, InlineDispatcher.Instance);
            Application.Begin(top);
            for (var i = 0; i < 500; i++) top.Logs.Append($"log {i}");
            Application.LayoutAndDraw(true);
            var view = top.Logs.Transcript;
            view.SetFocus();
            var end = view.Viewport.Y;
            Assert.True(end > 0);

            Assert.True(Application.RaiseKeyDownEvent(Key.CursorUp));
            Assert.Equal(end - 1, view.Viewport.Y);
            Assert.True(Application.RaiseKeyDownEvent(Key.PageUp));
            Assert.True(view.Viewport.Y < end - 1);
            Assert.True(Application.RaiseKeyDownEvent(Key.Home));
            Assert.Equal(0, view.Viewport.Y);
            Assert.True(Application.RaiseKeyDownEvent(Key.CursorDown));
            Assert.Equal(1, view.Viewport.Y);
            Assert.True(Application.RaiseKeyDownEvent(Key.PageDown));
            Assert.True(view.Viewport.Y > 1);
            Assert.True(Application.RaiseKeyDownEvent(Key.End));
            Assert.Equal(end, view.Viewport.Y);
        }
        finally
        {
            Application.Shutdown();
        }
    }

    [Fact]
    public void ShouldExpandTabsToTheNextStop_WhenARowIsDrawn()
    {
        Application.Init(driverName: "ansi");
        try
        {
            using var top = new Terminal.Gui.Views.Window();
            using var view = new TranscriptView(10) { Width = Terminal.Gui.ViewBase.Dim.Fill(), Height = Terminal.Gui.ViewBase.Dim.Fill() };
            top.Add(view);
            Application.Begin(top);
            view.AppendLines(["A\tBC\tD"]);

            Application.LayoutAndDraw(true);

            var screen = Application.Driver!.ToString() ?? string.Empty;
            Assert.Contains("A   BC  D", screen, StringComparison.Ordinal);
            Assert.True(PaneClipboard.SelectAll(view));
            Assert.Equal("A\tBC\tD", view.SelectedText);
        }
        finally
        {
            Application.Shutdown();
        }
    }
#pragma warning restore CS0618
}
