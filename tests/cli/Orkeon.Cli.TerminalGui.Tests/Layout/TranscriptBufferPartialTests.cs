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
}
