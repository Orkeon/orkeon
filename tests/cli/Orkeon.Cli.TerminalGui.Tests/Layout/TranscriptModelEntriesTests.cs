using Orkeon.Cli.TerminalGui.Layout;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

public class TranscriptModelEntriesTests
{
    [Fact]
    public void A_prompt_is_set_off_by_a_blank_row_and_each_of_its_lines_sits_on_a_bar()
    {
        // Act
        var lines = TranscriptModel.UserPrompt(GlyphSet.Unicode, "one\ntwo");

        // Assert
        Assert.Equal(["", "❯ one", "  two"], lines.Select(l => l.Text));
        Assert.Null(lines[0].RowRole);
        Assert.Equal(PaletteRole.UserBar, lines[1].RowRole);
        Assert.Equal(PaletteRole.UserBar, lines[2].RowRole);
    }

    [Fact]
    public void A_prompt_wraps_under_its_text_and_copies_without_its_marker()
    {
        // Act
        var line = TranscriptModel.UserPrompt(GlyphSet.Unicode, "typed")[1];

        // Assert
        Assert.Equal(2, line.HangingIndent);
        Assert.Equal("typed", line.CopySource);
    }

    [Fact]
    public void A_short_turn_gets_no_closing_line()
    {
        // Act
        var line = TranscriptModel.TurnEnd(GlyphSet.Unicode, TimeSpan.FromSeconds(9), 100);

        // Assert
        Assert.Null(line);
    }

    [Fact]
    public void A_long_turn_closes_on_a_dim_line_with_its_time_and_tokens()
    {
        // Act
        var line = TranscriptModel.TurnEnd(GlyphSet.Unicode, TimeSpan.FromSeconds(224), 118_399);

        // Assert
        Assert.Equal("  Done in 3m 44s · ↓ 118.3k tokens", line!.Text);
        Assert.Equal(PaletteRole.Dim, line.Runs[0].Role);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public void A_turn_without_tokens_says_only_its_time(long? tokens)
    {
        // Act
        var line = TranscriptModel.TurnEnd(GlyphSet.Unicode, TimeSpan.FromSeconds(12), tokens);

        // Assert
        Assert.Equal("  Done in 12s", line!.Text);
    }
}
