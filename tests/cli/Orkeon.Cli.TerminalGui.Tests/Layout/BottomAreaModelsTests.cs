using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Terminal.Gui.Drawing;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

// Pure models of the bottom area: separators, spinner split, palette, glyph fallbacks and the
// transcript lines written about tasks. No Terminal.Gui application is initialised.
public class SeparatorModelTests
{
    [Fact]
    public void Without_a_recipient_the_separator_is_a_dim_rule_across_the_full_width()
    {
        var segments = SeparatorModel.Compose(GlyphSet.Unicode, 12, null);
        var rule = Assert.Single(segments);
        Assert.Equal(new string('─', 12), rule.Text);
        Assert.Equal(PaletteRole.Dim, rule.Role);
    }

    [Fact]
    public void A_recipient_is_embedded_at_the_right_end_in_the_accent()
    {
        var segments = SeparatorModel.Compose(GlyphSet.Unicode, 20, "worker");
        Assert.Equal(new string('─', 9) + "── worker ─", RowSegment.Concat(segments));
        Assert.Equal(PaletteRole.Dim, segments[0].Role);
        Assert.Equal("── worker ─", segments[1].Text);
        Assert.Equal(PaletteRole.Accent, segments[1].Role);
    }

    [Fact]
    public void A_long_recipient_is_truncated_so_the_separator_keeps_its_width()
    {
        var text = RowSegment.Concat(SeparatorModel.Compose(GlyphSet.Unicode, 12, "a-very-long-task-name"));
        Assert.Equal("── a-very… ─", text);
    }

    [Fact]
    public void The_ascii_set_draws_the_separator_with_dashes()
    {
        Assert.Equal("------", RowSegment.Concat(SeparatorModel.Compose(GlyphSet.Ascii, 6, "  ")));
        Assert.Empty(SeparatorModel.Compose(GlyphSet.Ascii, 0, "worker"));
    }
}

public class SpinnerLineSplitTests
{
    [Fact]
    public void The_glyph_and_verb_are_the_head_and_the_parenthesis_the_tail()
    {
        var line = StatusLineFormatter.Compose(GlyphSet.Unicode, "Reasoning", TimeSpan.FromSeconds(279), 75_800, head: "✻");
        var (head, tail) = StatusLineFormatter.SplitHead(line);
        Assert.Equal("✻ Reasoning…", head);
        Assert.Equal(" (4m 39s · ↓ 75.8k tokens)", tail);
    }

    [Fact]
    public void A_line_without_an_ellipsis_is_all_head()
    {
        Assert.Equal(("plain", ""), StatusLineFormatter.SplitHead("plain"));
        Assert.Equal(("", ""), StatusLineFormatter.SplitHead(""));
    }
}

public class PaletteTests
{
    [Theory]
    [InlineData("truecolor", true)]
    [InlineData("24bit", true)]
    [InlineData("TrueColor", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("256color", false)]
    public void Truecolor_is_detected_from_COLORTERM(string? value, bool expected)
    {
        Assert.Equal(expected, SchemeFactory.IsTrueColor(value));
    }

    [Theory]
    [InlineData(PaletteRole.Dim, ColorName16.DarkGray)]
    [InlineData(PaletteRole.Accent, ColorName16.BrightRed)]
    [InlineData(PaletteRole.Ok, ColorName16.Green)]
    [InlineData(PaletteRole.Alert, ColorName16.Yellow)]
    [InlineData(PaletteRole.Error, ColorName16.Red)]
    public void Without_truecolor_every_role_falls_back_to_a_named_ansi_colour(PaletteRole role, ColorName16 expected)
    {
        Assert.Equal(new Color(expected), SchemeFactory.Foreground(role, trueColor: false));
    }

    [Fact]
    public void Main_text_and_every_background_keep_the_terminals_own_colours()
    {
        Assert.Equal(Color.None, SchemeFactory.Foreground(PaletteRole.Normal, trueColor: true));
        Assert.Equal(Color.None, SchemeFactory.Attr(PaletteRole.Accent, trueColor: true, bold: false, italic: false).Background);
        Assert.Equal(Color.None, SchemeFactory.Pane().Normal.Background);
        Assert.Equal(Color.None, SchemeFactory.Editable().Focus.Background);
    }

    [Fact]
    public void Bold_and_italic_reach_the_attribute_style()
    {
        var attr = SchemeFactory.Attr(PaletteRole.Dim, trueColor: false, bold: true, italic: true);
        Assert.True(attr.Style.HasFlag(TextStyle.Bold));
        Assert.True(attr.Style.HasFlag(TextStyle.Italic));
    }

    [Fact]
    public void The_selection_stays_visible_on_the_terminals_background()
    {
        var highlight = SchemeFactory.Dim().Highlight;
        Assert.NotEqual(Color.None, highlight.Foreground);
        Assert.NotEqual(Color.None, highlight.Background);
    }
}

public class BottomAreaGlyphTests
{
    [Fact]
    public void Each_marker_has_the_specified_ascii_fallback()
    {
        var u = GlyphSet.Unicode;
        var a = GlyphSet.Ascii;
        Assert.Equal(("❯", ">"), (u.Prompt, a.Prompt));
        Assert.Equal(("●", "*"), (u.Bullet, a.Bullet));
        Assert.Equal(("◯", "o"), (u.BulletHollow, a.BulletHollow));
        Assert.Equal(("✔", "v"), (u.Check, a.Check));
        Assert.Equal(("⏵⏵", ">>"), (u.Chevrons, a.Chevrons));
        Assert.Equal(("⎿", "|"), (u.Elbow, a.Elbow));
        Assert.Equal(("※", "*"), (u.Recap, a.Recap));
        Assert.Equal(("─", "-"), (u.RuleCell, a.RuleCell));
    }
}

public class TaskTranscriptTrackerTests
{
    private static AgentRowInfo Task(string ticket, string name = "worker@crew", string intent = "checks the design", int seconds = 135)
        => new() { Name = name, Description = intent, Ticket = ticket, Elapsed = TimeSpan.FromSeconds(seconds), State = TaskRowState.Running };

    private static readonly AgentRowInfo Main = new() { Name = "main", IsActive = true };

    [Fact]
    public void A_task_that_appears_is_reported_as_backgrounded()
    {
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode);

        var lines = tracker.Observe([Main, Task("T-1")]);

        Assert.Equal(["● worker@crew(checks the design)", "  ⎿  Backgrounded agent (↓ tasks)"], lines);
    }

    [Fact]
    public void A_task_that_leaves_the_list_is_reported_finished_with_its_last_duration()
    {
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode);
        tracker.Observe([Main, Task("T-1", seconds: 120)]);
        tracker.Observe([Main, Task("T-1", seconds: 135)]);

        var lines = tracker.Observe([]);

        Assert.Equal(["● Agent \"worker@crew\" finished · 2m 15s"], lines);
    }

    [Fact]
    public void A_poll_that_changes_nothing_writes_nothing_and_main_is_never_reported()
    {
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode);
        tracker.Observe([Main, Task("T-1")]);

        Assert.Empty(tracker.Observe([Main, Task("T-1", seconds: 200)]));
        Assert.Empty(new TaskTranscriptTracker(GlyphSet.Unicode).Observe([Main]));
    }

    [Fact]
    public void Finished_tasks_come_before_launched_ones_in_the_same_poll()
    {
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode);
        tracker.Observe([Main, Task("T-1", name: "old")]);

        var lines = tracker.Observe([Main, Task("T-2", name: "new", intent: "")]);

        Assert.Equal(["● Agent \"old\" finished · 2m 15s", "● new", "  ⎿  Backgrounded agent (↓ tasks)"], lines);
    }

    [Fact]
    public void The_ascii_set_writes_ascii_markers()
    {
        var tracker = new TaskTranscriptTracker(GlyphSet.Ascii);
        tracker.Observe([Task("T-1")]);
        var finished = Assert.Single(tracker.Observe([]));
        Assert.Equal("* Agent \"worker@crew\" finished - 2m 15s", finished);
    }
}

public class InterruptedLineTests
{
    [Fact]
    public void The_interrupt_leaves_a_line_asking_what_Orkeon_should_do_instead()
    {
        Assert.Equal("  ⎿  Interrupted · What should Orkeon do instead?", TranscriptModel.Interrupted(GlyphSet.Unicode));
        Assert.Equal("  |  Interrupted - What should Orkeon do instead?", TranscriptModel.Interrupted(GlyphSet.Ascii));
    }
}
