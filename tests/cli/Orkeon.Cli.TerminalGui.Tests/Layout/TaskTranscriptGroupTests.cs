using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Xunit;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public sealed class TaskTranscriptGroupTests
{
    private static AgentRowInfo Task(string ticket, string intent = "reads")
        => new() { Name = $"w{ticket}", Description = intent, Ticket = ticket, Elapsed = TimeSpan.FromSeconds(5) };

    [Fact]
    public void Tasks_seen_in_one_poll_read_as_one_group()
    {
        // Arrange
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode);

        // Act
        var lines = tracker.Observe([Task("1"), Task("2", intent: ""), Task("3")]);

        // Assert
        Assert.Equal(
            ["● 3 agents launched (↓ to manage)", "  ├─ w1(reads)", "  ├─ w2", "  └─ w3(reads)"],
            lines);
    }

    [Fact]
    public void A_group_draws_its_branches_in_ascii_when_the_terminal_has_no_unicode()
    {
        // Arrange
        var tracker = new TaskTranscriptTracker(GlyphSet.Ascii);

        // Act
        var lines = tracker.Observe([Task("1"), Task("2")]);

        // Assert
        Assert.Equal(["* 2 agents launched (v to manage)", "  +- w1(reads)", "  +- w2(reads)"], lines);
    }

    [Fact]
    public void An_end_line_counts_the_tools_of_its_task()
    {
        // Arrange
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode) { ToolCalls = _ => 3 };

        // Act
        var line = tracker.FinishedLine(Task("1"));

        // Assert
        Assert.Equal("● Agent \"w1\" finished · 3 tools · 5s", line);
    }

    [Fact]
    public void An_end_line_says_one_tool_in_the_singular()
    {
        // Arrange
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode) { ToolCalls = _ => 1 };

        // Act
        var line = tracker.FinishedLine(Task("1"));

        // Assert
        Assert.Contains("· 1 tool ·", line, StringComparison.Ordinal);
    }

    [Fact]
    public void An_end_line_says_the_tokens_of_its_task_when_it_knows_them()
    {
        // Arrange
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode);
        var row = new AgentRowInfo { Name = "w", Ticket = "1", Tokens = 1200 };

        // Act
        var line = tracker.FinishedLine(row);

        // Assert
        Assert.Equal($"● Agent \"w\" finished · {StatusLineFormatter.FormatTokens(1200)}", line);
    }

    [Theory]
    [InlineData("● Agent \"w\" finished · 5s", PaletteRole.Ok)]
    [InlineData("✗ Agent \"w\" failed · 5s", PaletteRole.Error)]
    [InlineData("⊘ Agent \"w\" cancelled · 5s", PaletteRole.Dim)]
    [InlineData("● 2 agents launched (↓ to manage)", PaletteRole.Accent)]
    public void The_glyph_of_a_line_takes_the_colour_of_what_it_says(string text, PaletteRole role)
    {
        // Arrange
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode);

        // Act
        var line = tracker.Styled(text);

        // Assert
        Assert.Equal(new TranscriptRun(0, 1, role), Assert.Single(line.Runs));
    }

    [Fact]
    public void A_branch_is_dim_and_the_name_on_it_is_not()
    {
        // Arrange
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode);

        // Act
        var line = tracker.Styled("  ├─ w1(reads)");

        // Assert
        Assert.Equal(new TranscriptRun(0, 4, PaletteRole.Dim), Assert.Single(line.Runs));
    }

    [Fact]
    public void The_elbow_line_of_a_single_launch_is_dim_to_its_end()
    {
        // Arrange
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode);
        const string text = "  ⎿  Backgrounded agent (↓ tasks)";

        // Act
        var line = tracker.Styled(text);

        // Assert
        Assert.Equal(new TranscriptRun(0, text.Length, PaletteRole.Dim), Assert.Single(line.Runs));
    }
}
