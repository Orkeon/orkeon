using Orkeon.Cli.TerminalGui.Layout;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class MarkdownInlineTests
{
    [Theory]
    [InlineData("plain text", "plain text")]
    [InlineData("a **bold** word", "a bold word")]
    [InlineData("an *italic* word", "an italic word")]
    [InlineData("some `code` here", "some code here")]
    [InlineData("snake_case_name stays", "snake_case_name stays")]
    [InlineData("a [label](http://x.y)", "a label (http://x.y)")]
    [InlineData("a &amp; b", "a & b")]
    [InlineData("1. not a list here", "1. not a list here")]
    public void The_text_shown_loses_its_markers(string source, string shown)
    {
        // Arrange
        var line = source;

        // Act
        var (text, _) = MarkdownInline.Parse(line);

        // Assert
        Assert.Equal(shown, text);
    }

    [Fact]
    public void Bold_covers_its_words()
    {
        // Arrange
        const string source = "a **bold** word";

        // Act
        var (_, runs) = MarkdownInline.Parse(source);

        // Assert
        Assert.Equal(new TranscriptRun(2, 4, PaletteRole.Normal, Bold: true), Assert.Single(runs));
    }

    [Fact]
    public void Code_takes_its_role()
    {
        // Arrange
        const string source = "`x`";

        // Act
        var (_, runs) = MarkdownInline.Parse(source);

        // Assert
        Assert.Equal(PaletteRole.Code, Assert.Single(runs).Role);
    }

    [Fact]
    public void Bold_inside_italic_keeps_both()
    {
        // Arrange
        const string source = "*a **b***";

        // Act
        var (_, runs) = MarkdownInline.Parse(source);

        // Assert
        Assert.Equal(new TranscriptRun(2, 1, PaletteRole.Normal, Bold: true, Italic: true), runs[^1]);
    }

    [Fact]
    public void An_unclosed_marker_stays_as_written()
    {
        // Arrange
        const string source = "a **half";

        // Act
        var (text, _) = MarkdownInline.Parse(source);

        // Assert
        Assert.Equal(source, text);
    }
}
