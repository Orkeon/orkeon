using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class ReplPaneViewMarkdownTests
{
    private static ReplPaneView CreatePane() => new(new TerminalGuiOptions(), InlineDispatcher.Instance);

    [Fact]
    public void A_finished_line_replaces_what_was_shown_of_it()
    {
        // Arrange
        using var pane = CreatePane();
        pane.AppendStream("a **b");

        // Act
        pane.AppendStream("**\n");

        // Assert
        Assert.Equal("a b\n", pane.DisplayedText);
    }

    [Fact]
    public void An_unfinished_line_is_shown()
    {
        // Arrange
        using var pane = CreatePane();

        // Act
        pane.AppendStream("a **b");

        // Assert
        Assert.Equal("a **b", pane.DisplayedText);
    }

    [Fact]
    public void Another_write_finishes_the_line_in_flight()
    {
        // Arrange
        using var pane = CreatePane();
        pane.AppendStream("**a**");

        // Act
        pane.AppendOutput("next\n");

        // Assert
        Assert.Equal("a\nnext\n", pane.DisplayedText);
    }

    [Fact]
    public void A_table_comes_out_when_another_write_ends_it()
    {
        // Arrange
        using var pane = CreatePane();
        pane.AppendStream("| a |\n|---|\n| b |\n");

        // Act
        pane.AppendOutput("next\n");

        // Assert
        Assert.Contains(" b", pane.DisplayedText, StringComparison.Ordinal);
    }

    [Fact]
    public void The_end_of_the_turn_finishes_the_line_in_flight()
    {
        // Arrange
        using var pane = CreatePane();
        pane.AppendStream("**a**");

        // Act
        pane.AfterSteps(() => { });

        // Assert
        Assert.Equal("a\n", pane.DisplayedText);
    }

    [Fact]
    public void A_clear_forgets_what_is_held()
    {
        // Arrange
        using var pane = CreatePane();
        pane.AppendStream("| a |");

        // Act
        pane.Clear();

        // Assert
        Assert.Equal(string.Empty, pane.DisplayedText);
    }

    [Theory]
    [InlineData("&#10;")]
    [InlineData("&NewLine;")]
    public void A_decoded_line_break_stays_on_its_line(string entity)
    {
        // Arrange
        using var pane = CreatePane();

        // Act
        pane.AppendStream($"use {entity} here\n");

        // Assert
        Assert.Equal("use   here\n", pane.DisplayedText);
    }

    private static void StreamIn(ReplPaneView pane, string thread, string text)
    {
        var previous = Orkeon.Cli.Abstractions.Console.ConsoleThread.Current;
        try
        {
            Orkeon.Cli.Abstractions.Console.ConsoleThread.Current = thread;
            pane.AppendStream(text);
        }
        finally
        {
            Orkeon.Cli.Abstractions.Console.ConsoleThread.Current = previous;
        }
    }

    [Fact]
    public void The_end_of_the_main_turn_leaves_the_fence_of_a_thread_still_streaming_open()
    {
        // Arrange
        using var pane = CreatePane();
        StreamIn(pane, "T-1", "```\ncode\n");
        pane.AfterSteps(() => { });

        // Act
        StreamIn(pane, "T-1", "```\n**after**\n");

        // Assert
        Assert.Equal("  code\n\nafter\n", pane.ThreadText("T-1"));
    }

    [Fact]
    public void The_end_of_the_main_turn_leaves_the_line_a_thread_is_writing_unfinished()
    {
        // Arrange
        using var pane = CreatePane();
        StreamIn(pane, "T-1", "**par");
        pane.AfterSteps(() => { });

        // Act
        StreamIn(pane, "T-1", "tial**\n");

        // Assert
        Assert.Equal("partial\n", pane.ThreadText("T-1"));
    }

    [Fact]
    public void The_end_of_a_task_finishes_what_its_stream_holds()
    {
        // Arrange
        using var pane = CreatePane();
        StreamIn(pane, "T-1", "**a**");

        // Act
        ((ITaskListNavigator)pane).RowsRefreshed([new AgentRowInfo { Name = "w@c", Ticket = "T-1", State = TaskRowState.Done }]);

        // Assert
        Assert.Equal("a\n", pane.ThreadText("T-1"));
    }

    [Fact]
    public void A_command_result_in_flight_keeps_its_elbow_when_the_console_reads_again()
    {
        // Arrange
        using var pane = CreatePane();
        var read = pane.ReadLineAsync(CancellationToken.None);
        pane.CurrentInput = "/x";
        pane.RaiseKeyDown(new Terminal.Gui.Input.Key(Terminal.Gui.Drivers.KeyCode.Enter));
        pane.AppendStream("**a**");

        // Act
        var next = pane.ReadLineAsync(CancellationToken.None);
        pane.AppendOutput("next\n");

        // Assert
        Assert.Contains("⎿  a\n", pane.DisplayedText, StringComparison.Ordinal);
    }
}
