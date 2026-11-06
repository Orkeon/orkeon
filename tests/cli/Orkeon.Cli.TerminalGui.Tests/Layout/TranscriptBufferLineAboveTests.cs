using Orkeon.Cli.TerminalGui.Layout;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class TranscriptBufferLineAboveTests
{
    private static TranscriptLine Bar(string text) => new(text) { RowRole = PaletteRole.UserBar };

    private static TranscriptBuffer Buffer(params TranscriptLine[] lines)
    {
        var buffer = new TranscriptBuffer(100);
        buffer.AppendStyled(lines);
        return buffer;
    }

    [Fact]
    public void A_block_of_several_lines_is_recalled_by_its_first_line()
    {
        // Arrange
        var buffer = Buffer(Bar("one"), Bar("two"), new TranscriptLine("reply"), new TranscriptLine("more"));

        // Act
        var line = buffer.LineAbove(3, PaletteRole.UserBar);

        // Assert
        Assert.Equal("one", line);
    }

    [Fact]
    public void The_block_the_row_starts_with_is_not_above_it()
    {
        // Arrange
        var buffer = Buffer(new TranscriptLine("intro"), Bar("prompt"));

        // Act
        var line = buffer.LineAbove(1, PaletteRole.UserBar);

        // Assert
        Assert.Null(line);
    }

    [Fact]
    public void A_row_past_the_content_has_nothing_above_it()
    {
        // Arrange
        var buffer = Buffer(Bar("prompt"));

        // Act
        var line = buffer.LineAbove(5, PaletteRole.UserBar);

        // Assert
        Assert.Null(line);
    }

    [Fact]
    public void The_nearest_block_is_the_one_recalled()
    {
        // Arrange
        var buffer = Buffer(Bar("old"), new TranscriptLine("a"), Bar("new"), new TranscriptLine("b"));

        // Act
        var line = buffer.LineAbove(3, PaletteRole.UserBar);

        // Assert
        Assert.Equal("new", line);
    }

    [Fact]
    public void A_prompt_that_opens_the_view_is_not_covered_by_the_one_before()
    {
        // Arrange
        var buffer = Buffer(Bar("old"), new TranscriptLine("reply"), Bar("new"), new TranscriptLine("more"));

        // Act
        var line = buffer.LineAbove(2, PaletteRole.UserBar);

        // Assert
        Assert.Null(line);
    }

    [Fact]
    public void The_second_line_of_a_block_on_top_recalls_the_first()
    {
        // Arrange
        var buffer = Buffer(Bar("one"), Bar("two"), new TranscriptLine("reply"));

        // Act
        var line = buffer.LineAbove(1, PaletteRole.UserBar);

        // Assert
        Assert.Equal("one", line);
    }
}
