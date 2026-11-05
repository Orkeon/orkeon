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
}
