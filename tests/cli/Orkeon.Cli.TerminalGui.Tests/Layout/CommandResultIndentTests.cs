using Orkeon.Cli.TerminalGui.Layout;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public class CommandResultIndentTests
{
    private static CommandResultIndent Indent(bool active = true)
    {
        var indent = new CommandResultIndent(GlyphSet.Unicode);
        indent.Reset(active);
        return indent;
    }

    [Fact]
    public void The_first_line_opens_on_the_elbow_and_the_next_ones_align_with_it()
    {
        // Arrange
        var indent = Indent();

        // Act
        var shown = indent.Apply("one\ntwo\n");

        // Assert
        Assert.Equal("  ⎿  one\n     two\n", shown);
    }

    [Fact]
    public void A_line_cut_across_two_writes_is_indented_once()
    {
        // Arrange
        var indent = Indent();
        indent.Apply("on");

        // Act
        var shown = indent.Apply("e\ntwo");

        // Assert
        Assert.Equal("e\n     two", shown);
    }

    [Fact]
    public void A_blank_line_stays_blank()
    {
        // Arrange
        var indent = Indent();

        // Act
        var shown = indent.Apply("\n\none");

        // Assert
        Assert.Equal("\n\n  ⎿  one", shown);
    }

    [Fact]
    public void Text_passes_untouched_when_no_result_is_in_flight()
    {
        // Arrange
        var indent = Indent(active: false);

        // Act
        var shown = indent.Apply("one\ntwo");

        // Assert
        Assert.Equal("one\ntwo", shown);
    }

    [Fact]
    public void The_elbow_comes_back_for_the_next_command()
    {
        // Arrange
        var indent = Indent();
        indent.Apply("one\n");
        indent.Reset(true);

        // Act
        var shown = indent.Apply("two");

        // Assert
        Assert.Equal("  ⎿  two", shown);
    }
}
