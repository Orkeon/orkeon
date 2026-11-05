using Orkeon.Cli.TerminalGui.Layout;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public class TranscriptBufferStyledTests
{
    private static TranscriptBuffer Buffer(int width = 20, int capacity = 100)
    {
        var buffer = new TranscriptBuffer(capacity);
        buffer.SetLayout(width, wordWrap: true);
        return buffer;
    }

    private static string RowText(TranscriptBuffer buffer, int row)
    {
        var slice = buffer.GetRow(row)!.Value;
        return slice.Text[slice.Start..slice.End];
    }

    [Fact]
    public void Styled_lines_take_the_open_empty_line_instead_of_leaving_a_blank_row()
    {
        // Arrange
        var buffer = Buffer();
        buffer.Append("before\n");

        // Act
        buffer.AppendStyled([new TranscriptLine("styled")]);

        // Assert
        Assert.Equal("before\nstyled\n", buffer.Text);
    }

    [Fact]
    public void Styled_lines_start_a_new_line_after_an_unfinished_stream_line()
    {
        // Arrange
        var buffer = Buffer();
        buffer.Append("partial");

        // Act
        buffer.AppendStyled([new TranscriptLine("styled")]);

        // Assert
        Assert.Equal("partial\nstyled\n", buffer.Text);
    }

    [Fact]
    public void Stream_text_written_after_styled_lines_does_not_extend_them()
    {
        // Arrange
        var buffer = Buffer();
        buffer.AppendStyled([new TranscriptLine("styled") { RowRole = PaletteRole.UserBar }]);

        // Act
        buffer.Append("next");

        // Assert
        Assert.Equal("styled\nnext", buffer.Text);
        Assert.Null(buffer.GetRow(1)!.Value.RowRole);
    }

    [Fact]
    public void Styled_lines_count_against_the_capacity_and_are_trimmed_like_any_line()
    {
        // Arrange
        var buffer = Buffer(capacity: 2);

        // Act
        buffer.AppendStyled([new TranscriptLine("a"), new TranscriptLine("b"), new TranscriptLine("c")]);

        // Assert
        Assert.Equal("b\nc\n", buffer.Text);
        Assert.Equal(2, buffer.LineCount);
    }

    [Fact]
    public void A_line_break_in_a_styled_line_is_refused()
    {
        // Arrange
        var buffer = Buffer();

        // Act
        var refused = Record.Exception(() => buffer.AppendStyled([new TranscriptLine("a\nb")]));

        // Assert
        Assert.IsType<ArgumentException>(refused);
    }

    [Fact]
    public void A_row_gives_its_runs_and_its_row_role()
    {
        // Arrange
        var buffer = Buffer();
        TranscriptRun[] runs = [new TranscriptRun(0, 2, PaletteRole.Dim)];

        // Act
        buffer.AppendStyled([new TranscriptLine("ab") { Runs = runs, RowRole = PaletteRole.DiffAdded }]);

        // Assert
        var row = buffer.GetRow(0)!.Value;
        Assert.Equal(runs, row.Runs);
        Assert.Equal(PaletteRole.DiffAdded, row.RowRole);
    }

    [Fact]
    public void A_hanging_line_wraps_its_continuation_rows_to_the_columns_left_by_the_indent()
    {
        // Arrange
        var buffer = Buffer(width: 10);

        // Act
        buffer.AppendStyled([new TranscriptLine("0123456789abcdefghijklmno") { HangingIndent = 2 }]);

        // Assert
        Assert.Equal("0123456789", RowText(buffer, 0));
        Assert.Equal(0, buffer.GetRow(0)!.Value.Indent);
        Assert.Equal("abcdefgh", RowText(buffer, 1));
        Assert.Equal(2, buffer.GetRow(1)!.Value.Indent);
        Assert.Equal("ijklmno", RowText(buffer, 2));
    }

    [Fact]
    public void A_word_too_long_for_the_narrower_continuation_row_is_cut_where_the_first_row_ends()
    {
        // Arrange
        var buffer = Buffer(width: 16);

        // Act
        buffer.AppendStyled([new TranscriptLine("a bcdefghijklmnopq") { HangingIndent = 8 }]);

        // Assert
        Assert.Equal("a bcdefghijklmno", RowText(buffer, 0));
        Assert.Equal("pq", RowText(buffer, 1));
    }

    [Fact]
    public void A_hanging_indent_that_would_starve_the_continuation_rows_is_dropped()
    {
        // Arrange
        var buffer = Buffer(width: 8);

        // Act
        buffer.AppendStyled([new TranscriptLine("0123456789") { HangingIndent = 4 }]);

        // Assert
        Assert.Equal(0, buffer.GetRow(1)!.Value.Indent);
    }

    [Fact]
    public void A_hit_on_a_continuation_row_counts_from_the_text_after_the_indent()
    {
        // Arrange
        var buffer = Buffer(width: 10);
        buffer.AppendStyled([new TranscriptLine("0123456789abcdefgh") { HangingIndent = 2 }]);

        // Act
        var position = buffer.HitTest(1, 3);

        // Assert
        Assert.Equal(11, position.Offset);
    }

    [Fact]
    public void A_hit_in_the_indent_lands_on_the_first_char_of_the_row()
    {
        // Arrange
        var buffer = Buffer(width: 10);
        buffer.AppendStyled([new TranscriptLine("0123456789abcdefgh") { HangingIndent = 2 }]);

        // Act
        var position = buffer.HitTest(1, 0);

        // Assert
        Assert.Equal(10, position.Offset);
    }

    [Fact]
    public void A_copy_of_a_whole_line_returns_its_copy_source()
    {
        // Arrange
        var buffer = Buffer();
        buffer.AppendStyled([new TranscriptLine("> typed") { CopySource = "typed" }, new TranscriptLine("next")]);
        var first = buffer.Start;

        // Act
        var copied = buffer.GetText(first, new TranscriptPosition(first.Line + 1, 4));

        // Assert
        Assert.Equal("typed\nnext", copied);
    }

    [Fact]
    public void A_copy_of_a_part_of_a_line_returns_what_is_shown()
    {
        // Arrange
        var buffer = Buffer();
        buffer.AppendStyled([new TranscriptLine("> typed") { CopySource = "typed" }]);
        var line = buffer.Start.Line;

        // Act
        var copied = buffer.GetText(new TranscriptPosition(line, 2), new TranscriptPosition(line, 5));

        // Assert
        Assert.Equal("typ", copied);
    }

    [Fact]
    public void Stream_lines_ignore_the_styled_wrap_and_stay_as_they_were()
    {
        // Arrange
        var buffer = Buffer(width: 10);

        // Act
        buffer.Append("0123456789abcdefgh");

        // Assert
        Assert.Equal("abcdefgh", RowText(buffer, 1));
        Assert.Equal(0, buffer.GetRow(1)!.Value.Indent);
        Assert.Empty(buffer.GetRow(1)!.Value.Runs);
    }
}
