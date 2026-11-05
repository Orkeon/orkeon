using Orkeon.Cli.TerminalGui.Layout;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class MarkdownLinesTests
{
    private const string Table = "| Name | Size |\n|:---|---:|\n| a | 1 |\n| bb | 22 |";

    private static List<TranscriptLine> Rendered(string source, int width = 80)
    {
        var markdown = new MarkdownLines(GlyphSet.Ascii);
        var lines = new List<TranscriptLine>();
        foreach (var line in source.Split('\n'))
            lines.AddRange(markdown.Feed(line, width));
        lines.AddRange(markdown.Flush(width));
        return lines;
    }

    [Fact]
    public void A_heading_is_bold_without_its_marker()
    {
        // Arrange
        const string source = "## Title";

        // Act
        var line = Assert.Single(Rendered(source));

        // Assert
        Assert.Equal(("Title", true), (line.Text, line.Runs[0].Bold));
    }

    [Theory]
    [InlineData("- item", "- item", 2)]
    [InlineData("* item", "- item", 2)]
    [InlineData("  + item", "  - item", 4)]
    [InlineData("3. item", "3. item", 3)]
    public void A_list_item_hangs_under_its_text(string source, string shown, int hang)
    {
        // Arrange
        var text = source;

        // Act
        var line = Assert.Single(Rendered(text));

        // Assert
        Assert.Equal((shown, hang), (line.Text, line.HangingIndent));
    }

    [Fact]
    public void A_quote_sits_behind_a_bar()
    {
        // Arrange
        const string source = "> said";

        // Act
        var line = Assert.Single(Rendered(source));

        // Assert
        Assert.Equal(GlyphSet.Ascii.SideBar + " said", line.Text);
    }

    [Fact]
    public void A_rule_stays_short_of_the_width()
    {
        // Arrange
        const string source = "---";

        // Act
        var line = Assert.Single(Rendered(source, width: 10));

        // Assert
        Assert.Equal(10, line.Text.Length);
    }

    [Fact]
    public void Code_keeps_its_markers_and_takes_its_role()
    {
        // Arrange
        const string source = "```\n**not bold**\n```";

        // Act
        var lines = Rendered(source);

        // Assert
        Assert.Equal(("  **not bold**", PaletteRole.Code), (lines[0].Text, lines[0].Runs[0].Role));
    }

    [Fact]
    public void A_fence_names_its_language()
    {
        // Arrange
        const string source = "```csharp\nx\n```";

        // Act
        var lines = Rendered(source);

        // Assert
        Assert.Equal("csharp", lines[0].Text);
    }

    [Fact]
    public void A_copy_of_a_fence_gives_back_the_source()
    {
        // Arrange
        const string source = "```\nx\n```";

        // Act
        var lines = Rendered(source);

        // Assert
        Assert.Equal(source, string.Join("\n", lines.Select(l => l.CopySource)));
    }

    [Fact]
    public void A_copy_of_a_styled_line_gives_back_the_source()
    {
        // Arrange
        const string source = "a **b**";

        // Act
        var line = Assert.Single(Rendered(source));

        // Assert
        Assert.Equal(source, line.CopySource);
    }

    [Fact]
    public void A_table_draws_a_box_with_its_columns_aligned()
    {
        // Arrange
        var source = Table;

        // Act
        var lines = Rendered(source);

        // Assert
        Assert.Equal(
            ["+------+------+", "| Name | Size |", "+------+------+", "| a    |    1 |", "| bb   |   22 |", "+------+------+"],
            lines.Select(l => l.Text));
    }

    [Fact]
    public void A_copy_of_a_table_gives_back_the_source()
    {
        // Arrange
        var source = Table;

        // Act
        var lines = Rendered(source);

        // Assert
        Assert.Equal("\n" + source + "\n", string.Join("\n", lines.Select(l => l.CopySource)));
    }

    [Fact]
    public void A_table_header_is_bold()
    {
        // Arrange
        var source = Table;

        // Act
        var lines = Rendered(source);

        // Assert
        Assert.Contains(lines[1].Runs, r => r.Bold && lines[1].Text.Substring(r.Start, r.Length) == "Name");
    }

    [Fact]
    public void A_table_too_wide_reads_row_by_row_with_nothing_cut()
    {
        // Arrange
        var source = Table;

        // Act
        var lines = Rendered(source, width: 8);

        // Assert
        Assert.Equal(["Name: a", "Size: 1", "", "Name: bb", "Size: 22"], lines.Select(l => l.Text));
    }

    [Fact]
    public void A_bar_inside_code_or_escaped_does_not_cut_a_cell()
    {
        // Arrange
        const string source = "| a |\n|---|\n| `x|y` |\n| p\\|q |";

        // Act
        var lines = Rendered(source);

        // Assert
        Assert.Equal(("| x|y |", "| p|q |"), (lines[3].Text, lines[4].Text));
    }

    [Fact]
    public void A_line_with_a_bar_and_no_table_is_a_paragraph()
    {
        // Arrange
        const string source = "a | b\nnext";

        // Act
        var lines = Rendered(source);

        // Assert
        Assert.Equal(["a | b", "next"], lines.Select(l => l.Text));
    }

    [Fact]
    public void A_table_waits_for_the_line_that_ends_it()
    {
        // Arrange
        var markdown = new MarkdownLines(GlyphSet.Ascii);
        markdown.Feed("| a |", 80);
        markdown.Feed("|---|", 80);

        // Act
        var lines = markdown.Feed("| b |", 80);

        // Assert
        Assert.Empty(lines);
    }

    [Fact]
    public void Text_after_a_table_comes_after_it()
    {
        // Arrange
        const string source = "| a |\n|---|\n| b |\nafter";

        // Act
        var lines = Rendered(source);

        // Assert
        Assert.Equal("after", lines[^1].Text);
    }

    [Fact]
    public void A_flush_keeps_a_fence_open()
    {
        // Arrange
        var markdown = new MarkdownLines(GlyphSet.Ascii);
        markdown.Feed("```", 80);
        markdown.Flush(80);

        // Act
        var lines = markdown.Feed("**x**", 80);

        // Assert
        Assert.Equal("  **x**", lines[0].Text);
    }
}
