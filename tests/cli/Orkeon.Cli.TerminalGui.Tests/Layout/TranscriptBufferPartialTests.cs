using Orkeon.Cli.TerminalGui.Layout;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class TranscriptBufferPartialTests
{
    private static TranscriptBuffer Buffer() => new(capacity: 100);

    [Fact]
    public void A_partial_takes_the_place_of_the_one_before()
    {
        // Arrange
        var buffer = Buffer();
        buffer.ReplacePartial([], "ab");

        // Act
        buffer.ReplacePartial([], "abc");

        // Assert
        Assert.Equal("abc", buffer.Text);
    }

    [Fact]
    public void Finished_lines_take_the_place_of_the_partial()
    {
        // Arrange
        var buffer = Buffer();
        buffer.ReplacePartial([], "a **b");

        // Act
        buffer.ReplacePartial([new TranscriptLine("a b")], "next");

        // Assert
        Assert.Equal("a b\nnext", buffer.Text);
    }

    [Fact]
    public void A_partial_starts_its_own_line_after_plain_text()
    {
        // Arrange
        var buffer = Buffer();
        buffer.Append("plain");

        // Act
        buffer.ReplacePartial([], "part");

        // Assert
        Assert.Equal("plain\npart", buffer.Text);
    }

    [Fact]
    public void A_partial_uses_the_open_empty_line()
    {
        // Arrange
        var buffer = Buffer();
        buffer.Append("plain\n");

        // Act
        buffer.ReplacePartial([], "part");

        // Assert
        Assert.Equal("plain\npart", buffer.Text);
    }

    [Fact]
    public void Another_write_keeps_the_partial_as_a_line()
    {
        // Arrange
        var buffer = Buffer();
        buffer.ReplacePartial([], "part");
        buffer.AppendStyled([new TranscriptLine("styled")]);

        // Act
        buffer.ReplacePartial([], "next");

        // Assert
        Assert.Equal("part\nstyled\nnext", buffer.Text);
    }

    [Fact]
    public void Taking_a_partial_back_counts_no_line_twice()
    {
        // Arrange
        var buffer = Buffer();
        buffer.ReplacePartial([], "a");
        buffer.ReplacePartial([], "ab");

        // Act
        buffer.ReplacePartial([], "abc");

        // Assert
        Assert.Equal(1, buffer.LineCount);
    }

    [Fact]
    public void A_line_break_in_a_partial_reads_as_a_blank()
    {
        // Arrange
        var buffer = Buffer();

        // Act
        buffer.ReplacePartial([], "a\nb");

        // Assert
        Assert.Equal("a b", buffer.Text);
    }

    [Fact]
    public void A_partial_that_shrinks_leaves_the_width_of_the_line_above()
    {
        // Arrange
        var buffer = Buffer();
        buffer.SetLayout(0, wordWrap: false);
        buffer.AppendStyled([new TranscriptLine("12345678")]);
        buffer.ReplacePartial([], "1234567890");

        // Act
        buffer.ReplacePartial([], "ab");

        // Assert
        Assert.Equal(8, buffer.MaxColumns);
    }

    [Fact]
    public void A_partial_counts_in_the_width_while_it_is_the_widest()
    {
        // Arrange
        var buffer = Buffer();
        buffer.SetLayout(0, wordWrap: false);
        buffer.AppendStyled([new TranscriptLine("123")]);

        // Act
        buffer.ReplacePartial([], "1234567890");

        // Assert
        Assert.Equal(10, buffer.MaxColumns);
    }

    [Fact]
    public void A_partial_made_a_line_by_another_write_keeps_its_width()
    {
        // Arrange
        var buffer = Buffer();
        buffer.SetLayout(0, wordWrap: false);
        buffer.ReplacePartial([], "1234567890");

        // Act
        buffer.AppendStyled([new TranscriptLine("ab")]);

        // Assert
        Assert.Equal(10, buffer.MaxColumns);
    }

    [Fact]
    public void A_partial_that_grows_wraps_only_what_it_adds()
    {
        // Arrange
        var buffer = Buffer();
        buffer.SetLayout(80, wordWrap: true);
        var text = new string('x', 2000);

        // Act
        for (var length = 1; length <= text.Length; length++) buffer.ReplacePartial([], text[..length]);

        // Assert
        Assert.True(buffer.CharactersWrapped < 200_000, $"{buffer.CharactersWrapped} characters wrapped");
    }

    [Fact]
    public void A_partial_grown_piece_by_piece_wraps_as_if_written_whole()
    {
        // Arrange
        const string text = "some words\tthat wrap over more than one row of this narrow view";
        var grown = Buffer();
        grown.SetLayout(12, wordWrap: true);
        var whole = Buffer();
        whole.SetLayout(12, wordWrap: true);
        whole.ReplacePartial([], text);

        // Act
        for (var length = 1; length <= text.Length; length++) grown.ReplacePartial([], text[..length]);

        // Assert
        Assert.Equal(
            Enumerable.Range(0, whole.RowCount).Select(whole.GetRow),
            Enumerable.Range(0, grown.RowCount).Select(grown.GetRow));
    }
}
