using Orkeon.Cli.Abstractions.Console;
using Orkeon.Cli.TerminalGui.Layout;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class StepActivityModelTests
{
    private static StepActivityModel StepWith(params (string Tool, string Subject)[] calls)
    {
        var step = new StepActivityModel(GlyphSet.Ascii);
        var id = 0L;
        foreach (var (tool, subject) in calls)
            step.Start(new ToolCallStarted(++id, tool, null) { Subject = subject });
        return step;
    }

    private static string RowText(IReadOnlyList<RowSegment> row) => RowSegment.Concat(row);

    [Fact]
    public void An_empty_step_shows_nothing()
    {
        // Arrange
        var step = StepWith();

        // Act
        var rows = step.Rows(80, 5);

        // Assert
        Assert.Empty(rows);
    }

    [Fact]
    public void The_first_row_counts_the_calls_of_the_step()
    {
        // Arrange
        var step = StepWith(("file_read", "a.cs"), ("shell_command", "ls"), ("shell_command", "pwd"));

        // Act
        var rows = step.Rows(80, 5);

        // Assert
        Assert.Equal("* Read 1 file, ran 2 shell commands", RowText(rows[0]));
    }

    [Fact]
    public void A_call_is_shown_with_what_it_works_on()
    {
        // Arrange
        var step = StepWith(("file_read", "a.cs"), ("shell_command", "ls"));

        // Act
        var rows = step.Rows(80, 5);

        // Assert
        Assert.Equal(["  | Read a.cs", "    $ ls"], rows.Skip(1).Select(RowText));
    }

    [Fact]
    public void A_tool_with_no_phrase_shows_its_name()
    {
        // Arrange
        var step = StepWith(("web_fetch", ""));

        // Act
        var rows = step.Rows(80, 5);

        // Assert
        Assert.Equal("  | web_fetch", RowText(rows[1]));
    }

    [Fact]
    public void Only_the_latest_calls_are_listed_when_room_is_short()
    {
        // Arrange
        var step = StepWith(("shell_command", "one"), ("shell_command", "two"), ("shell_command", "three"));

        // Act
        var rows = step.Rows(80, 3);

        // Assert
        Assert.Equal(["  | $ two", "    $ three"], rows.Skip(1).Select(RowText));
    }

    [Fact]
    public void A_long_call_is_cut_to_the_width()
    {
        // Arrange
        var step = StepWith(("shell_command", new string('x', 100)));

        // Act
        var rows = step.Rows(20, 5);

        // Assert
        Assert.Equal("  | $ xxxxxxxxxxx...", RowText(rows[1]));
    }

    [Fact]
    public void A_failed_call_says_so()
    {
        // Arrange
        var step = StepWith(("file_read", "a.cs"));
        step.Finish(new ToolCallFinished(1, null, Succeeded: false));

        // Act
        var rows = step.Rows(80, 5);

        // Assert
        Assert.Equal("  | Read a.cs (failed)", RowText(rows[1]));
    }

    [Fact]
    public void The_end_of_an_unknown_call_is_ignored()
    {
        // Arrange
        var step = StepWith(("file_read", "a.cs"));

        // Act
        step.Finish(new ToolCallFinished(99, null, Succeeded: true));

        // Assert
        Assert.True(step.HasOpenCall);
    }

    [Fact]
    public void A_step_is_open_until_its_last_call_ends()
    {
        // Arrange
        var step = StepWith(("file_read", "a.cs"), ("file_read", "b.cs"));

        // Act
        step.Finish(new ToolCallFinished(1, null, Succeeded: true));

        // Assert
        Assert.True(step.HasOpenCall);
    }

    [Fact]
    public void Folding_leaves_a_sentence_of_the_calls()
    {
        // Arrange
        var step = StepWith(("file_read", "a.cs"), ("shell_command", "ls"));

        // Act
        var folded = step.Fold();

        // Assert
        Assert.Equal("  Read 1 file, ran 1 shell command", folded.Summary.Text);
    }

    [Fact]
    public void Folding_makes_the_numbers_of_the_sentence_bold()
    {
        // Arrange
        var step = StepWith(("shell_command", "ls"));

        // Act
        var folded = step.Fold();

        // Assert
        var bold = folded.Summary.Runs.Where(r => r.Bold).Select(r => folded.Summary.Text.Substring(r.Start, r.Length));
        Assert.Equal(["1"], bold);
    }

    [Fact]
    public void Folding_keeps_each_call_for_the_details()
    {
        // Arrange
        var step = StepWith(("file_read", "a.cs"), ("shell_command", "ls"));

        // Act
        var folded = step.Fold();

        // Assert
        Assert.Equal(["  | Read a.cs", "    $ ls"], folded.Details.Select(l => l.Text));
    }

    [Fact]
    public void Folding_counts_the_calls_by_tool()
    {
        // Arrange
        var step = StepWith(("shell_command", "ls"), ("shell_command", "pwd"));

        // Act
        var folded = step.Fold();

        // Assert
        Assert.Equal(2, folded.Counts["shell_command"]);
    }

    [Fact]
    public void Folding_empties_the_step()
    {
        // Arrange
        var step = StepWith(("shell_command", "ls"));

        // Act
        step.Fold();

        // Assert
        Assert.True(step.IsEmpty);
    }
}
