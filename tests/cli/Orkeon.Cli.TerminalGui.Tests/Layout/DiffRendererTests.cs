using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Layout;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class DiffRendererTests
{
    private static string Marked(TranscriptLine line, PaletteRole role)
        => string.Concat(line.Runs.Where(r => r.Role == role).Select(r => line.Text.Substring(r.Start, r.Length)));

    private static IReadOnlyList<TranscriptLine> Rendered(string before, string after)
        => DiffRenderer.Rows(LineDiff.Compute(before, after), GlyphSet.Ascii);

    [Fact]
    public void An_added_line_is_numbered_and_painted()
    {
        // Arrange
        DiffRow[] rows = [new(DiffRowKind.Added, null, 12, "text")];

        // Act
        var line = DiffRenderer.Rows(rows, GlyphSet.Ascii)[0];

        // Assert
        Assert.Equal(("  12 + text", PaletteRole.DiffAdded), (line.Text, line.RowRole));
    }

    [Fact]
    public void A_removed_line_is_painted_with_its_old_number()
    {
        // Arrange
        DiffRow[] rows = [new(DiffRowKind.Removed, 7, null, "text")];

        // Act
        var line = DiffRenderer.Rows(rows, GlyphSet.Ascii)[0];

        // Assert
        Assert.Equal(("  7 - text", PaletteRole.DiffRemoved), (line.Text, line.RowRole));
    }

    [Fact]
    public void Numbers_of_different_widths_line_up()
    {
        // Arrange
        DiffRow[] rows = [new(DiffRowKind.Context, 9, 9, "a"), new(DiffRowKind.Added, null, 10, "b")];

        // Act
        var lines = DiffRenderer.Rows(rows, GlyphSet.Ascii);

        // Assert
        Assert.Equal("   9   a", lines[0].Text);
    }

    [Fact]
    public void A_wrapped_row_continues_under_its_text()
    {
        // Arrange
        DiffRow[] rows = [new(DiffRowKind.Added, null, 12, "text")];

        // Act
        var line = DiffRenderer.Rows(rows, GlyphSet.Ascii)[0];

        // Assert
        Assert.Equal("  12 + ".Length, line.HangingIndent);
    }

    [Fact]
    public void A_tab_is_expanded_before_the_words_are_marked()
    {
        // Arrange
        DiffRow[] rows = [new(DiffRowKind.Added, null, 1, "\tb")];

        // Act
        var line = DiffRenderer.Rows(rows, GlyphSet.Ascii)[0];

        // Assert
        Assert.Equal("  1 +     b", line.Text);
    }

    [Fact]
    public void Only_the_word_that_changed_is_marked_on_the_new_line()
    {
        // Arrange
        const string before = "var count = 1;\n";
        const string after = "var total = 1;\n";

        // Act
        var lines = Rendered(before, after);

        // Assert
        Assert.Equal("total", Marked(lines[1], PaletteRole.AddedWord));
    }

    [Fact]
    public void Only_the_word_that_changed_is_marked_on_the_old_line()
    {
        // Arrange
        const string before = "var count = 1;\n";
        const string after = "var total = 1;\n";

        // Act
        var lines = Rendered(before, after);

        // Assert
        Assert.Equal("count", Marked(lines[0], PaletteRole.RemovedWord));
    }

    [Fact]
    public void A_line_rewritten_entirely_marks_no_word()
    {
        // Arrange
        const string before = "completely different\n";
        const string after = "nothing alike here\n";

        // Act
        var lines = Rendered(before, after);

        // Assert
        Assert.Empty(lines[1].Runs);
    }

    [Fact]
    public void A_gap_shows_the_ellipsis()
    {
        // Arrange
        DiffRow[] rows = [new(DiffRowKind.Gap, null, null, string.Empty)];

        // Act
        var line = DiffRenderer.Rows(rows, GlyphSet.Ascii)[0];

        // Assert
        Assert.Equal("  " + GlyphSet.Ascii.Ellipsis, line.Text);
    }

    [Fact]
    public void A_long_diff_says_how_many_lines_it_leaves_out()
    {
        // Arrange
        var rows = Enumerable.Range(1, 100).Select(i => new DiffRow(DiffRowKind.Added, null, i, "x")).ToList();

        // Act
        var lines = DiffRenderer.Rows(rows, GlyphSet.Ascii);

        // Assert
        Assert.Equal($"  {GlyphSet.Ascii.Ellipsis} +20 lines", lines[^1].Text);
    }

    [Fact]
    public void A_call_opens_on_the_file_and_its_counts()
    {
        // Arrange
        var diff = new ToolCallDiff("src/a.cs", LineDiff.Compute("a\n", "b\nc\n"));

        // Act
        var lines = DiffRenderer.ForCall(diff, GlyphSet.Ascii);

        // Assert
        Assert.Equal($"{GlyphSet.Ascii.Bullet} src/a.cs +2 -1", lines[0].Text);
    }

    [Fact]
    public void A_patch_takes_its_numbers_from_the_hunk()
    {
        // Arrange
        const string patch = "@@ -10,2 +20,2 @@\n ctx\n-old\n+new";

        // Act
        var lines = DiffRenderer.Unified(patch);

        // Assert
        Assert.Equal(["@@ -10,2 +20,2 @@", "  20   ctx", "  11 - old", "  21 + new"], lines.Select(l => l.Text));
    }

    [Fact]
    public void A_copy_of_a_patch_line_gives_back_the_patch()
    {
        // Arrange
        const string patch = "@@ -1 +1 @@\n+new";

        // Act
        var lines = DiffRenderer.Unified(patch);

        // Assert
        Assert.Equal("+new", lines[1].CopySource);
    }

    [Fact]
    public void A_patch_without_a_hunk_stays_as_it_is_painted()
    {
        // Arrange
        const string patch = "+added";

        // Act
        var lines = DiffRenderer.Unified(patch);

        // Assert
        Assert.Equal(("+added", PaletteRole.DiffAdded), (lines[0].Text, lines[0].RowRole));
    }
}
