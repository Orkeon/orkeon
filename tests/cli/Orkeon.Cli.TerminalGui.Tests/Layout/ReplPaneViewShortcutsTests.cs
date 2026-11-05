using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.Input;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class ReplPaneViewShortcutsTests
{
    private static ReplPaneView CreatePane() => new(new TerminalGuiOptions(), InlineDispatcher.Instance);

    [Fact]
    public void A_question_mark_on_an_empty_prompt_shows_the_shortcuts()
    {
        // Arrange
        using var pane = CreatePane();

        // Act
        pane.RaiseKeyDown(new Key('?'));

        // Assert
        Assert.Contains("shift+tab", pane.DisplayedText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_question_mark_on_an_empty_prompt_types_nothing()
    {
        // Arrange
        using var pane = CreatePane();
        var key = new Key('?');

        // Act
        pane.RaiseKeyDown(key);

        // Assert
        Assert.True(key.Handled);
    }

    [Fact]
    public void A_question_mark_in_a_draft_is_a_character_of_the_draft()
    {
        // Arrange
        using var pane = CreatePane();
        pane.CurrentInput = "why";
        var key = new Key('?');

        // Act
        pane.RaiseKeyDown(key);

        // Assert
        Assert.False(key.Handled);
    }

    [Fact]
    public void The_help_paints_its_lines_in_the_secondary_colour()
    {
        // Act
        var lines = ShortcutsHelp.Lines(GlyphSet.Ascii);

        // Assert
        Assert.All(lines, line => Assert.Equal(PaletteRole.Dim, Assert.Single(line.Runs).Role));
    }

    [Fact]
    public void The_help_aligns_the_effects()
    {
        // Act
        var lines = ShortcutsHelp.Lines(GlyphSet.Unicode);

        // Assert
        Assert.Equal(lines[0].Text.IndexOf("commands", StringComparison.Ordinal), lines[6].Text.IndexOf("logs", StringComparison.Ordinal));
    }

    [Fact]
    public void A_task_counts_the_tool_calls_of_its_screen()
    {
        // Arrange
        using var pane = CreatePane();
        pane.Started(new Orkeon.Cli.Abstractions.Console.ToolCallStarted(1, "t", "T-1"));

        // Act
        pane.Started(new Orkeon.Cli.Abstractions.Console.ToolCallStarted(2, "t", "T-1"));

        // Assert
        Assert.Equal(2, pane.ToolCallsOf("T-1"));
    }

    [Fact]
    public void Ctrl_end_at_the_tail_stays_with_the_draft()
    {
        // Arrange
        using var pane = CreatePane();
        var key = Key.End.WithCtrl;

        // Act
        pane.RaiseKeyDown(key);

        // Assert
        Assert.False(key.Handled);
    }
}
