using Orkeon.Cli.TerminalGui.Layout;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class MarkdownStreamTests
{
    [Fact]
    public void An_unfinished_line_comes_out_as_it_stands()
    {
        // Arrange
        var stream = new MarkdownStream(GlyphSet.Ascii);

        // Act
        var (lines, partial) = stream.Write("a **b", 80);

        // Assert
        Assert.Equal((0, "a **b"), (lines.Count, partial));
    }

    [Fact]
    public void A_line_cut_in_pieces_is_read_whole()
    {
        // Arrange
        var stream = new MarkdownStream(GlyphSet.Ascii);
        stream.Write("a **b", 80);

        // Act
        var (lines, _) = stream.Write("**\n", 80);

        // Assert
        Assert.Equal("a b", Assert.Single(lines).Text);
    }

    [Fact]
    public void A_table_row_being_written_stays_hidden()
    {
        // Arrange
        var stream = new MarkdownStream(GlyphSet.Ascii);

        // Act
        var (_, partial) = stream.Write("| a", 80);

        // Assert
        Assert.Equal(string.Empty, partial);
    }

    [Fact]
    public void A_flush_gives_the_line_still_open()
    {
        // Arrange
        var stream = new MarkdownStream(GlyphSet.Ascii);
        stream.Write("**end**", 80);

        // Act
        var lines = stream.Flush(80);

        // Assert
        Assert.Equal("end", Assert.Single(lines).Text);
    }

    [Fact]
    public void A_flush_leaves_it_empty()
    {
        // Arrange
        var stream = new MarkdownStream(GlyphSet.Ascii);
        stream.Write("| a |", 80);

        // Act
        stream.Flush(80);

        // Assert
        Assert.True(stream.IsEmpty);
    }

    [Fact]
    public void A_carriage_return_does_not_reach_the_screen()
    {
        // Arrange
        var stream = new MarkdownStream(GlyphSet.Ascii);

        // Act
        var (lines, _) = stream.Write("a\r\n", 80);

        // Assert
        Assert.Equal("a", Assert.Single(lines).Text);
    }
}
