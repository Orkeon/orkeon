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

    // ── Review fixes: the finished feed (L1), the final state (L3), ASCII hints (L7) ─────

    private static AgentRowInfo Ended(string ticket, TaskRowState state, string name = "worker@crew", string intent = "checks the design", int seconds = 135)
        => new() { Name = name, Description = intent, Ticket = ticket, Elapsed = TimeSpan.FromSeconds(seconds), State = state };

    [Fact]
    public void A_task_that_starts_and_ends_between_two_polls_gets_both_lines_in_order()
    {
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode);
        tracker.Observe([Main], []);
        var quick = Ended("T-9", TaskRowState.Done, name: "quick", intent: "one shot", seconds: 3);

        var lines = tracker.Observe([], [quick]);

        Assert.Equal(["● quick(one shot)", "  ⎿  Backgrounded agent (↓ tasks)", "● Agent \"quick\" finished · 3s"], lines);
        Assert.Empty(tracker.Observe([], [quick]));
    }

    [Fact]
    public void Tasks_already_finished_at_the_first_poll_are_not_reported()
    {
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode);

        Assert.Empty(tracker.Observe([], [Ended("T-0", TaskRowState.Done)]));
    }

    [Fact]
    public void A_task_that_failed_or_was_cancelled_says_so_with_the_glyph_of_its_state()
    {
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode);
        tracker.Observe([Main, Task("T-1", name: "a"), Task("T-2", name: "b")], []);

        var lines = tracker.Observe([], [Ended("T-1", TaskRowState.Failed, name: "a"), Ended("T-2", TaskRowState.Cancelled, name: "b", seconds: 4)]);

        Assert.Equal(["✗ Agent \"a\" failed · 2m 15s", "⊘ Agent \"b\" cancelled · 4s"], lines);
    }

    [Fact]
    public void A_task_still_in_the_rows_but_already_finished_is_reported_finished_once()
    {
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode);
        tracker.Observe([Main, Task("T-1")], []);

        // It completed between the rows read and the finished read of the same poll.
        var lines = tracker.Observe([Main, Task("T-1")], [Ended("T-1", TaskRowState.Done)]);

        Assert.Equal(["● Agent \"worker@crew\" finished · 2m 15s"], lines);
        Assert.Empty(tracker.Observe([], [Ended("T-1", TaskRowState.Done)]));
    }

    [Fact]
    [Trait("BR", "BR-03")]
    public void ShouldReportLaunchAndEndLinesOnce_WhenEndedRowsAreListed()
    {
        var tracker = new TaskTranscriptTracker(GlyphSet.Unicode);
        var ended = Ended("T-1", TaskRowState.Done, name: "w", intent: "");
        var all = new List<string>();
        all.AddRange(tracker.Observe([Main, Task("T-1", name: "w", intent: "")], []));
        all.AddRange(tracker.Observe([Main], [ended]));
        all.AddRange(tracker.Observe([Main], [ended]));
        all.AddRange(tracker.Observe([Main], [ended]));

        Assert.Equal(["● w", "  ⎿  Backgrounded agent (↓ tasks)", "● Agent \"w\" finished · 2m 15s"], all);
    }

    [Fact]
    public void The_ascii_set_writes_ascii_state_glyphs_and_hints()
    {
        var tracker = new TaskTranscriptTracker(GlyphSet.Ascii);
        tracker.Observe([], []);

        var lines = tracker.Observe([], [Ended("T-1", TaskRowState.Failed)]);

        Assert.Equal(["* worker@crew(checks the design)", "  |  Backgrounded agent (v tasks)", "x Agent \"worker@crew\" failed - 2m 15s"], lines);
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

public class ColumnAwareRowsTests
{
    private static readonly AgentRowInfo Wide = new()
    {
        Name = "日本語のタスク",
        Description = "レビュー中です、長い説明文",
        Elapsed = TimeSpan.FromSeconds(135),
        Tokens = 1200,
        Ticket = "T-1",
    };

    [Fact]
    public void A_row_of_wide_characters_fills_exactly_the_width_in_columns_and_keeps_the_metrics()
    {
        var text = AgentsPaneModel.FormatRow(GlyphSet.Unicode, Wide, 50);

        Assert.Equal(50, TerminalText.Columns(text));
        Assert.EndsWith(AgentsPaneModel.ComposeMetrics(GlyphSet.Unicode, Wide), text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_emoji_in_the_name_does_not_shift_the_colour_of_the_cells_after_it()
    {
        var row = new AgentRowInfo { Name = "🚀 rocket", Elapsed = TimeSpan.FromSeconds(5), Ticket = "T-1" };
        var segments = AgentsPaneModel.Segments(GlyphSet.Unicode, row, 40);
        var text = RowSegment.Concat(segments);
        var metrics = AgentsPaneModel.ComposeMetrics(GlyphSet.Unicode, row);
        var firstMetricCell = TerminalText.Graphemes(text[..text.IndexOf(metrics, StringComparison.Ordinal)]).Count;

        Assert.Equal(PaletteRole.Dim, AgentsPaneModel.SegmentAtCell(segments, firstMetricCell)?.Role);
        Assert.Equal(PaletteRole.Normal, AgentsPaneModel.SegmentAtCell(segments, firstMetricCell - 1)?.Role);
        Assert.True(AgentsPaneModel.SegmentAtCell(segments, 4)?.Bold);
        Assert.Null(AgentsPaneModel.SegmentAtCell(segments, TerminalText.Graphemes(text).Count));
    }

    [Fact]
    public void On_a_tiny_width_the_name_goes_first_then_the_metrics_are_cut_with_an_ellipsis()
    {
        var text = AgentsPaneModel.FormatRow(GlyphSet.Unicode, Wide, 12);

        Assert.Equal(12, TerminalText.Columns(text));
        Assert.StartsWith("  ◯ 2m", text, StringComparison.Ordinal);
        Assert.EndsWith("…", text, StringComparison.Ordinal);
        Assert.DoesNotContain("日", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ascii_set_cuts_with_three_dots()
    {
        var row = new AgentRowInfo { Name = "worker", Description = new string('x', 80), Ticket = "T-1" };

        var text = AgentsPaneModel.FormatRow(GlyphSet.Ascii, row, 30);

        Assert.Equal(30, text.Length);
        Assert.EndsWith("...", text.TrimEnd(), StringComparison.Ordinal);
        Assert.DoesNotContain("…", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_glyph_set_carries_the_up_arrow_and_the_ellipsis_with_ascii_fallbacks()
    {
        Assert.Equal(("↑", "^"), (GlyphSet.Unicode.Up, GlyphSet.Ascii.Up));
        Assert.Equal(("…", "..."), (GlyphSet.Unicode.Ellipsis, GlyphSet.Ascii.Ellipsis));
    }

    [Fact]
    public void The_hint_bar_builds_its_arrows_from_the_glyph_set()
    {
        static string Bar(GlyphSet glyphs, bool focused)
            => RowSegment.Concat(HintBarModel.Segments(glyphs, "default", commandRunning: false, tasksAvailable: true, taskListFocused: focused));

        Assert.EndsWith("v tasks", Bar(GlyphSet.Ascii, focused: false), StringComparison.Ordinal);
        Assert.Equal("  ^/v select - enter view", Bar(GlyphSet.Ascii, focused: true));
        Assert.EndsWith("↓ tasks", Bar(GlyphSet.Unicode, focused: false), StringComparison.Ordinal);
        Assert.Equal("  ↑/↓ select · enter view", Bar(GlyphSet.Unicode, focused: true));
    }
    [Fact]
    public void The_separator_label_is_measured_in_columns_and_cut_with_the_glyph_set_ellipsis()
    {
        var ascii = RowSegment.Concat(SeparatorModel.Compose(GlyphSet.Ascii, 12, "a-very-long-task-name"));
        Assert.Equal("-- a-ve... -", ascii);
        Assert.DoesNotContain("…", ascii, StringComparison.Ordinal);

        var wide = RowSegment.Concat(SeparatorModel.Compose(GlyphSet.Unicode, 12, "日本語タスク名"));
        Assert.Equal(12, TerminalText.Columns(wide));
        Assert.Equal("── 日本語… ─", wide);
    }

    [Fact]
    public void The_model_witness_is_cut_in_columns_with_the_glyph_set_ellipsis()
    {
        var info = new ModelWitnessInfo { Model = new string('m', 40), Availability = Orkeon.Cli.Abstractions.ModelAvailability.Available };
        var ascii = HintBarModel.Witness(info, GlyphSet.Ascii);
        Assert.Equal(HintBarModel.WitnessMaxWidth, TerminalText.Columns(ascii));
        Assert.EndsWith("...", ascii, StringComparison.Ordinal);

        var wide = HintBarModel.Witness(new ModelWitnessInfo { Model = new string('模', 20), Availability = Orkeon.Cli.Abstractions.ModelAvailability.Available }, GlyphSet.Unicode);
        Assert.True(TerminalText.Columns(wide) <= HintBarModel.WitnessMaxWidth);
        Assert.EndsWith("…", wide, StringComparison.Ordinal);
    }
}
