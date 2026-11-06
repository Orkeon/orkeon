using Orkeon.Cli.Abstractions.Console;

namespace Orkeon.Cli.Abstractions.Tests.Console;

public sealed class LineDiffTests
{
    private static string Numbered(int count, Func<int, string>? line = null)
        => string.Concat(Enumerable.Range(1, count).Select(i => (line?.Invoke(i) ?? $"line {i}") + "\n"));

    [Fact]
    public void A_replaced_line_shows_the_old_one_then_the_new_one()
    {
        // Arrange
        const string before = "a\nb\nc\n";
        const string after = "a\nB\nc\n";

        // Act
        var rows = LineDiff.Compute(before, after);

        // Assert
        Assert.Equal(
            [
                new DiffRow(DiffRowKind.Context, 1, 1, "a"),
                new DiffRow(DiffRowKind.Removed, 2, null, "b"),
                new DiffRow(DiffRowKind.Added, null, 2, "B"),
                new DiffRow(DiffRowKind.Context, 3, 3, "c"),
            ],
            rows);
    }

    [Fact]
    public void The_same_text_has_no_row()
    {
        // Arrange
        const string text = "a\nb\n";

        // Act
        var rows = LineDiff.Compute(text, text);

        // Assert
        Assert.Empty(rows);
    }

    [Fact]
    public void Line_endings_alone_are_no_change()
    {
        // Arrange
        const string before = "a\r\nb\r\n";

        // Act
        var rows = LineDiff.Compute(before, "a\nb\n");

        // Assert
        Assert.Empty(rows);
    }

    [Fact]
    public void An_empty_old_side_is_all_added()
    {
        // Arrange
        const string after = "a\nb\n";

        // Act
        var rows = LineDiff.Compute(string.Empty, after);

        // Assert
        Assert.Equal([DiffRowKind.Added, DiffRowKind.Added], rows.Select(r => r.Kind));
    }

    [Fact]
    public void Only_three_lines_around_a_change_are_kept()
    {
        // Arrange
        var before = Numbered(20);
        var after = Numbered(20, i => i == 10 ? "changed" : $"line {i}");

        // Act
        var rows = LineDiff.Compute(before, after);

        // Assert
        Assert.Equal(6, rows.Count(r => r.Kind == DiffRowKind.Context));
    }

    [Fact]
    public void Two_distant_changes_are_separated_by_one_gap()
    {
        // Arrange
        var before = Numbered(30);
        var after = Numbered(30, i => i is 5 or 25 ? "changed" : $"line {i}");

        // Act
        var rows = LineDiff.Compute(before, after);

        // Assert
        Assert.Single(rows, r => r.Kind == DiffRowKind.Gap);
    }

    [Fact]
    public void The_counts_read_the_rows()
    {
        // Arrange
        var rows = LineDiff.Compute("a\nb\n", "a\nc\nd\n");

        // Act
        var diff = new ToolCallDiff("f", rows);

        // Assert
        Assert.Equal((2, 1), (diff.Added, diff.Removed));
    }

    [Fact]
    public void What_two_lists_share_is_marked_in_order()
    {
        // Arrange
        string[] left = ["a", "x", "b", "c"];
        string[] right = ["b", "a", "y", "c"];

        // Act
        var (keptLeft, keptRight) = LineDiff.Common(left, right);

        // Assert
        Assert.Equal(2, keptLeft.Count(k => k));
        Assert.Equal(2, keptRight.Count(k => k));
    }

    [Fact]
    public void A_middle_too_long_to_compare_reads_as_replaced()
    {
        // Arrange
        var left = Enumerable.Range(0, 1100).Select(i => $"l{i}").Append("shared").ToArray();
        var right = Enumerable.Range(0, 1100).Select(i => $"r{i}").Prepend("shared").ToArray();

        // Act
        var (keptLeft, _) = LineDiff.Common(left, right);

        // Assert
        Assert.DoesNotContain(true, keptLeft);
    }
}
