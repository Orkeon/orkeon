using Orkeon.Cli.TerminalGui.Hosting;
using Orkeon.Cli.TerminalGui.Layout;
using Orkeon.Cli.TerminalGui.Telemetry;

namespace Orkeon.Cli.TerminalGui.Tests.Layout;

// Pure models of the fidelity layout (PLAN phases 2-7). None of these tests loads a
// Terminal.Gui type, so they run everywhere the TUI-12 module-init bug does not.
public class GlyphSetTests
{
    [Fact]
    public void Auto_resolves_by_encoding()
    {
        Assert.Same(GlyphSet.Unicode, GlyphSet.Resolve(GlyphMode.Auto, outputIsUtf8: true));
        Assert.Same(GlyphSet.Ascii, GlyphSet.Resolve(GlyphMode.Auto, outputIsUtf8: false));
    }

    [Fact]
    public void Always_and_Never_ignore_the_encoding()
    {
        Assert.Same(GlyphSet.Unicode, GlyphSet.Resolve(GlyphMode.Always, outputIsUtf8: false));
        Assert.Same(GlyphSet.Ascii, GlyphSet.Resolve(GlyphMode.Never, outputIsUtf8: true));
    }

    [Fact]
    public void Ascii_set_is_pure_7bit()
    {
        // The whole point of the fallback: a non-UTF-8 console must never receive a
        // multi-byte marker in the pane an operator reads to debug.
        var markers = new List<string>
        {
            GlyphSet.Ascii.Prompt, GlyphSet.Ascii.Bullet, GlyphSet.Ascii.BulletHollow,
            GlyphSet.Ascii.Asterisk, GlyphSet.Ascii.Recap, GlyphSet.Ascii.Chevrons,
            GlyphSet.Ascii.RuleCell, GlyphSet.Ascii.Down, GlyphSet.Ascii.Dot,
            GlyphSet.Ascii.BarFilled, GlyphSet.Ascii.BarEmpty,
            GlyphSet.Ascii.Check, GlyphSet.Ascii.Cross, GlyphSet.Ascii.Slashed, GlyphSet.Ascii.Elbow,
        };
        markers.AddRange(GlyphSet.Ascii.SpinnerFrames);
        foreach (var s in markers)
        {
            Assert.All(s, c => Assert.True(c < 128, $"non-ASCII char in fallback: '{c}'"));
        }
    }
}

public class BannerComposerTests
{
    private static BannerInfo Info(string model = "kimi-k3 (1M context) · api.moonshot.ai") => new()
    {
        ProductLine = "Orkeon Coding Agent — orkeon-repl 0.9.2",
        ModelLine = model,
        WorkspaceLine = "/workspace",
        Tips = ["Switch models anytime with /model.", "+more · /status"],
    };

    [Fact]
    public void Renders_logo_beside_the_three_lines_then_tips()
    {
        var lines = BannerComposer.Compose(Info(), GlyphSet.Unicode);
        var text = string.Join("\n", lines);
        Assert.Contains("Orkeon Coding Agent", text, StringComparison.Ordinal);
        Assert.Contains("kimi-k3", text, StringComparison.Ordinal);
        Assert.Contains("/workspace", text, StringComparison.Ordinal);
        Assert.Contains("/model", text, StringComparison.Ordinal);
        // Logo present: at least one half-block row.
        Assert.Contains(lines, l => l.Contains('▄', StringComparison.Ordinal));
    }

    [Fact]
    public void Omits_an_empty_model_line_instead_of_placeholdering()
    {
        // A host with no Llm section omits the line — inventing "(unknown)" here is
        // exactly the dressed-up absence the /model fix removed.
        var lines = BannerComposer.Compose(Info(model: ""), GlyphSet.Unicode);
        Assert.DoesNotContain(lines, l => l.Contains("unknown", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(lines, l => l.Contains("/workspace", StringComparison.Ordinal));
    }

    [Fact]
    public void Ascii_glyphs_degrade_the_LOGO_to_7bit()
    {
        // Only the logo is the composer's to degrade — the info lines are the host's
        // own text and may legitimately carry accents ("Orkeon") or dashes.
        var lines = BannerComposer.Compose(Info(), GlyphSet.Ascii);
        Assert.DoesNotContain(lines, l => l.Contains('▄', StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains('▀', StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains(".---.", StringComparison.Ordinal));
    }
}

public class StatusLineFormatterTests
{
    [Theory]
    [InlineData(8, "8s")]
    [InlineData(0, "0s")]
    [InlineData(224, "3m 44s")]
    [InlineData(3725, "1h 02m")]
    public void FormatElapsed_uses_the_reference_three_formats(int seconds, string expected)
    {
        Assert.Equal(expected, StatusLineFormatter.FormatElapsed(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void FormatElapsed_clamps_negative_to_zero()
    {
        Assert.Equal("0s", StatusLineFormatter.FormatElapsed(TimeSpan.FromSeconds(-5)));
    }

    [Theory]
    [InlineData(126, "126 tokens")]
    [InlineData(999, "999 tokens")]
    [InlineData(1000, "1.0k tokens")]
    [InlineData(118_300, "118.3k tokens")]
    [InlineData(118_399, "118.3k tokens")] // truncated, not rounded up — the reference floors
    public void FormatTokens_switches_to_k_at_1000_and_floors(long tokens, string expected)
    {
        Assert.Equal(expected, StatusLineFormatter.FormatTokens(tokens));
    }

    [Fact]
    public void Compose_full_form_matches_the_capture_shape()
    {
        var line = StatusLineFormatter.Compose(
            GlyphSet.Unicode, "Reasoning", TimeSpan.FromSeconds(224), 118_300, TurnState.Streaming);
        Assert.Equal("✱ Reasoning… (3m 44s · ↓ 118.3k tokens · streaming)", line);
    }

    [Fact]
    public void Compose_omits_what_it_does_not_know()
    {
        // The short form of capture 2 — no state, and here not even tokens. An invented
        // "0 tokens" would read as a measurement.
        Assert.Equal("✱ Working… (8s)",
            StatusLineFormatter.Compose(GlyphSet.Unicode, "Working", TimeSpan.FromSeconds(8)));
    }

    [Fact]
    public void Gerund_is_stable_for_a_given_turn_seed()
    {
        Assert.Equal(StatusLineFormatter.GerundFor(42), StatusLineFormatter.GerundFor(42));
        Assert.All(StatusLineFormatter.Gerunds, g => Assert.False(string.IsNullOrWhiteSpace(g)));
    }

    [Fact]
    public void Compose_animated_head_replaces_the_asterisk()
    {
        var line = StatusLineFormatter.Compose(
            GlyphSet.Unicode, "Working", TimeSpan.FromSeconds(8), head: "✶");
        Assert.Equal("✶ Working… (8s)", line);
    }

    [Fact]
    public void ComposeProgress_with_ratio_renders_the_capture_bar()
    {
        // ▰▰▰▱▱▱▱▱▱▱ 34% — bar floors (0.349 → 3 cells / 34%): a bar that overstates
        // progress reads as stalled near the end, understating never does.
        var line = StatusLineFormatter.ComposeProgress(
            GlyphSet.Unicode, "Compacting conversation", 0.349, message: null,
            elapsed: TimeSpan.FromSeconds(12));
        Assert.Equal("✱ Compacting conversation… ▰▰▰▱▱▱▱▱▱▱ 34% (12s)", line);
    }

    [Fact]
    public void ComposeProgress_without_ratio_shows_elapsed_and_message()
    {
        var line = StatusLineFormatter.ComposeProgress(
            GlyphSet.Unicode, "Compacting conversation", ratio: null,
            message: "extracting memories", elapsed: TimeSpan.FromSeconds(12));
        Assert.Equal("✱ Compacting conversation… (12s · extracting memories)", line);
    }

    [Fact]
    public void ComposeProgress_clamps_the_ratio()
    {
        var line = StatusLineFormatter.ComposeProgress(
            GlyphSet.Unicode, "op", 1.7, null, TimeSpan.Zero);
        Assert.Contains("▰▰▰▰▰▰▰▰▰▰ 100%", line, StringComparison.Ordinal);
    }

    [Fact]
    public void VerbFor_uses_the_custom_rotation_and_advances_over_time()
    {
        // The spinnerVerbs setting (tweakcc's "thinking verbs"): stable within a
        // rotation period, moving to the next verb afterwards so long turns stay alive.
        string[] verbs = ["Reticulating", "Percolating"];
        var early = StatusLineFormatter.VerbFor(0, TimeSpan.FromSeconds(1), verbs);
        var stillEarly = StatusLineFormatter.VerbFor(0, TimeSpan.FromSeconds(14), verbs);
        var later = StatusLineFormatter.VerbFor(0, TimeSpan.FromSeconds(16), verbs);

        Assert.Equal(early, stillEarly);
        Assert.NotEqual(early, later);
        Assert.Contains(early, verbs);
        Assert.Contains(later, verbs);
    }

    [Fact]
    public void VerbFor_falls_back_to_the_gerunds_and_skips_blank_entries()
    {
        Assert.Contains(StatusLineFormatter.VerbFor(7, TimeSpan.Zero, null), StatusLineFormatter.Gerunds);
        Assert.Contains(StatusLineFormatter.VerbFor(7, TimeSpan.Zero, []), StatusLineFormatter.Gerunds);
        Assert.Equal("Real", StatusLineFormatter.VerbFor(0, TimeSpan.Zero, ["  ", "Real"]));
    }

    [Fact]
    public void SpinnerFrame_cycles_a_step_per_quarter_second()
    {
        var f0 = StatusLineFormatter.SpinnerFrame(GlyphSet.Unicode, TimeSpan.Zero);
        var f1 = StatusLineFormatter.SpinnerFrame(GlyphSet.Unicode, TimeSpan.FromMilliseconds(250));
        var wrapped = StatusLineFormatter.SpinnerFrame(GlyphSet.Unicode, TimeSpan.FromMilliseconds(1000));

        Assert.NotEqual(f0, f1);
        Assert.Equal(f0, wrapped);
        Assert.Contains(f0, GlyphSet.Unicode.SpinnerFrames);
    }
}

public class HintBarModelTests
{
    private static string Text(IReadOnlyList<RowSegment> segments) => RowSegment.Concat(segments);

    [Fact]
    public void Idle_bar_has_no_interrupt_entry()
    {
        var text = Text(HintBarModel.Segments(GlyphSet.Unicode, "default", commandRunning: false, tasksAvailable: true));
        Assert.DoesNotContain("interrupt", text, StringComparison.Ordinal);
        Assert.Contains("shift+tab", text, StringComparison.Ordinal);
        Assert.Contains("ctrl+g logs", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "CH-07")]
    public void Running_bar_names_the_escape_pair()
    {
        var text = Text(HintBarModel.Segments(GlyphSet.Unicode, "default", commandRunning: true, tasksAvailable: true));
        Assert.Contains("esc esc to interrupt", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_bar_is_indented_by_two_columns_and_separated_by_dim_dots()
    {
        var segments = HintBarModel.Segments(GlyphSet.Unicode, "plan", commandRunning: false, tasksAvailable: true);
        Assert.Equal("  ⏵⏵ plan mode on (shift+tab to cycle) · ctrl+g logs · ↓ tasks", Text(segments));
        Assert.All(segments.Where(s => s.Text.Trim() == "·"), s => Assert.Equal(PaletteRole.Dim, s.Role));
    }

    [Fact]
    public void Tasks_entry_appears_only_while_a_task_other_than_main_exists()
    {
        var with = HintBarModel.Segments(GlyphSet.Unicode, "default", commandRunning: false, tasksAvailable: true);
        var tasks = Assert.Single(with, s => s.Text == "↓ tasks");
        Assert.Equal(PaletteRole.Dim, tasks.Role);

        var without = HintBarModel.Segments(GlyphSet.Unicode, "default", commandRunning: false, tasksAvailable: false);
        Assert.DoesNotContain("tasks", Text(without), StringComparison.Ordinal);
    }

    [Fact]
    public void A_focused_list_replaces_every_other_entry_with_its_own_affordance()
    {
        var segments = HintBarModel.Segments(GlyphSet.Unicode, "bypassPermissions", commandRunning: true, tasksAvailable: true, taskListFocused: true);
        Assert.Equal("  ↑/↓ select · enter view", Text(segments));
        Assert.DoesNotContain(segments, s => s.Role == PaletteRole.Alert);
    }

    [Theory]
    [InlineData("default", "default permissions")]
    [InlineData("acceptEdits", "accept edits on")]
    [InlineData("plan", "plan mode on")]
    [InlineData("bypassPermissions", "bypass permissions on")]
    [InlineData("dontAsk", "don't ask on")]
    [InlineData("custom", "custom")]
    public void Every_permission_mode_reads_as_a_label(string mode, string label)
    {
        Assert.Equal(label, HintBarModel.PostureLabel(mode));
    }

    [Theory]
    [InlineData("bypassPermissions", true)]
    [InlineData("dontAsk", true)]
    [InlineData("acceptEdits", true)]
    [InlineData("default", false)]
    [InlineData("plan", false)]
    public void Only_a_permissive_mode_is_painted_in_the_alert_role(string mode, bool alert)
    {
        Assert.Equal(alert, HintBarModel.IsLoudPosture(mode));
        var posture = HintBarModel.Segments(GlyphSet.Unicode, mode, commandRunning: false, tasksAvailable: false)[1];
        Assert.Equal(alert ? PaletteRole.Alert : PaletteRole.Dim, posture.Role);
    }

    [Fact]
    public void Bypass_mode_reads_as_the_reference_label()
    {
        var posture = HintBarModel.Segments(GlyphSet.Unicode, "bypassPermissions", commandRunning: false, tasksAvailable: false)[1];
        Assert.Equal("⏵⏵ bypass permissions on (shift+tab to cycle)", posture.Text);
    }

    [Fact]
    public void Blank_mode_reads_default()
    {
        var text = Text(HintBarModel.Segments(GlyphSet.Unicode, "  ", commandRunning: false, tasksAvailable: false));
        Assert.StartsWith("  ⏵⏵ default permissions", text, StringComparison.Ordinal);
    }

    private static ModelWitnessInfo Info(string model, Orkeon.Cli.Abstractions.ModelAvailability availability)
        => new() { Model = model, Availability = availability };

    [Fact]
    [Trait("BR", "CH-08")]
    public void Witness_names_the_model_and_never_starts_with_a_slash()
    {
        var text = HintBarModel.Witness(Info("kimi-k2", Orkeon.Cli.Abstractions.ModelAvailability.Available));
        Assert.Equal("kimi-k2", text);
        Assert.False(text.StartsWith('/'));
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void Witness_availability_maps_to_green_amber_red()
    {
        var green = HintBarModel.WitnessScheme(Orkeon.Cli.Abstractions.ModelAvailability.Available).Normal;
        var amber = HintBarModel.WitnessScheme(Orkeon.Cli.Abstractions.ModelAvailability.Reconnecting).Normal;
        var red = HintBarModel.WitnessScheme(Orkeon.Cli.Abstractions.ModelAvailability.Unavailable).Normal;

        Assert.Equal(SchemeFactory.Ok().Normal, green);
        Assert.Equal(SchemeFactory.Warn().Normal, amber);
        Assert.Equal(SchemeFactory.Error().Normal, red);
        Assert.NotEqual(green, amber);
        Assert.NotEqual(green, red);
        Assert.NotEqual(amber, red);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void Witness_without_a_configured_model_is_unavailable()
    {
        Assert.Equal("no model", HintBarModel.Witness(null));
        Assert.Equal("no model", HintBarModel.Witness(Info("", Orkeon.Cli.Abstractions.ModelAvailability.Unavailable)));
        Assert.Equal("no model", HintBarModel.Witness(Info("   ", Orkeon.Cli.Abstractions.ModelAvailability.Unavailable)));
        Assert.Equal("gpt-5", HintBarModel.Witness(Info("gpt-5", Orkeon.Cli.Abstractions.ModelAvailability.Available)));
        Assert.Equal(
            SchemeFactory.Ok().Normal,
            HintBarModel.WitnessScheme(Orkeon.Cli.Abstractions.ModelAvailability.Available).Normal);
    }

    [Fact]
    [Trait("BR", "CH-08")]
    public void Witness_long_name_is_truncated_with_an_ellipsis()
    {
        var exact = new string('m', HintBarModel.WitnessMaxWidth);
        Assert.Equal(exact, HintBarModel.Witness(Info(exact, Orkeon.Cli.Abstractions.ModelAvailability.Available)));

        var shown = HintBarModel.Witness(Info(new string('m', 40), Orkeon.Cli.Abstractions.ModelAvailability.Available));
        Assert.Equal(new string('m', 23) + "…", shown);
        Assert.Equal(24, shown.Length);
    }
}

public class TranscriptModelTests
{
    [Fact]
    public void Assistant_turn_gets_the_bullet_on_the_first_line_only()
    {
        var lines = TranscriptModel.Render(GlyphSet.Unicode, TranscriptKind.AssistantTurn, "first\nsecond");
        Assert.Equal("● first", lines[0]);
        Assert.Equal("  second", lines[1]);
    }

    [Fact]
    public void User_turn_has_no_glyph_at_all()
    {
        // The captures show the operator's text bare — no marge column.
        var lines = TranscriptModel.Render(GlyphSet.Unicode, TranscriptKind.UserTurn, "do the thing");
        Assert.Equal("do the thing", lines[0]);
    }

    [Fact]
    public void Tool_activity_indents_to_the_text_column()
    {
        var lines = TranscriptModel.Render(GlyphSet.Unicode, TranscriptKind.ToolActivity, "Read 1 file");
        Assert.Equal("  Read 1 file", lines[0]);
    }

    [Fact]
    public void Continuation_indents_without_a_re_bullet()
    {
        var lines = TranscriptModel.Render(GlyphSet.Unicode, TranscriptKind.AssistantContinuation, "more prose");
        Assert.Equal("  more prose", lines[0]);
    }

    [Fact]
    public void SplitAssistantMessage_bullets_the_first_paragraph_only()
    {
        var parts = TranscriptModel.SplitAssistantMessage("intro\n\nsecond paragraph\n\nthird");
        Assert.Equal(TranscriptKind.AssistantTurn, parts[0].Kind);
        Assert.All(parts.Skip(1), p => Assert.Equal(TranscriptKind.AssistantContinuation, p.Kind));
        Assert.Equal(3, parts.Count);
    }

    [Fact]
    public void SplitAssistantMessage_is_empty_for_blank_input()
    {
        Assert.Empty(TranscriptModel.SplitAssistantMessage("   "));
    }
}

public class ToolActivityAggregatorTests
{
    private static KeyValuePair<string, int> Kv(string tool, int n) => new(tool, n);

    [Fact]
    public void Composes_the_reference_sentence()
    {
        var s = ToolActivityAggregator.ComposeSentence([Kv("shell_command", 9), Kv("file_read", 1)]);
        Assert.Equal("Read 1 file, ran 9 shell commands", s);
    }

    [Fact]
    public void Orders_reads_before_writes_before_shell()
    {
        var s = ToolActivityAggregator.ComposeSentence(
            [Kv("shell_command", 1), Kv("file_write", 2), Kv("file_read", 3)]);
        Assert.Equal("Read 3 files, wrote 2 files, ran 1 shell command", s);
    }

    [Fact]
    public void Unknown_tools_get_the_truthful_generic_phrase()
    {
        // "used codebase_map ×2" beats inventing a verb that might describe the wrong action.
        var s = ToolActivityAggregator.ComposeSentence([Kv("codebase_map", 2)]);
        Assert.Equal("Used codebase_map ×2", s);
    }

    [Fact]
    public void Empty_counts_render_nothing()
    {
        Assert.Equal(string.Empty, ToolActivityAggregator.ComposeSentence([]));
    }

    [Fact]
    public void Live_listener_counts_stopped_tool_spans_and_drains_once()
    {
        using var aggregator = new ToolActivityAggregator();
        using var source = new System.Diagnostics.ActivitySource("Orkeon.Scripting");

        using (var a = source.StartActivity("execute_tool file_read"))
        {
            a?.SetTag("gen_ai.operation.name", "execute_tool");
            a?.SetTag("gen_ai.tool.name", "file_read");
        }
        using (var a = source.StartActivity("execute_tool shell_command"))
        {
            a?.SetTag("gen_ai.operation.name", "execute_tool");
            a?.SetTag("gen_ai.tool.name", "shell_command");
        }
        using (var a = source.StartActivity("other.span")) { /* must not count */ }

        var sentence = aggregator.DrainSentence();
        Assert.Equal("Read 1 file, ran 1 shell command", sentence);
        // Drained: a second drain reports nothing — each sentence covers one turn.
        Assert.Equal(string.Empty, aggregator.DrainSentence());
    }
}

public class AgentsPaneModelTests
{
    private static AgentRowInfo Row(
        string name = "assistant@main-loop",
        string desc = "Tu vérifies l'exactitude d'un document de conception",
        bool active = false,
        long? tokens = 101_100)
        => new()
        {
            Name = name,
            Description = desc,
            Elapsed = TimeSpan.FromSeconds(224),
            Tokens = tokens,
            IsActive = active,
        };

    [Fact]
    public void The_recipient_row_gets_the_filled_bullet_and_the_others_the_hollow_one()
    {
        // Column 0 is the list cursor (blank at rest), column 2 the bullet.
        var line = AgentsPaneModel.FormatRow(GlyphSet.Unicode, Row(), 100, recipient: true);
        Assert.StartsWith("  ● ", line, StringComparison.Ordinal);
        var other = AgentsPaneModel.FormatRow(GlyphSet.Unicode, Row(), 100);
        Assert.StartsWith("  ◯ ", other, StringComparison.Ordinal);
    }

    [Fact]
    public void Metrics_are_right_aligned_and_dim_and_the_phrase_gives_way()
    {
        var segments = AgentsPaneModel.Segments(GlyphSet.Unicode, Row(), 60);
        var line = RowSegment.Concat(segments);
        Assert.Equal(60, line.Length);
        Assert.EndsWith("3m 44s · ↓ 101.1k tokens", line, StringComparison.Ordinal);
        Assert.Contains("…", line, StringComparison.Ordinal);
        Assert.Equal(PaletteRole.Dim, segments[^1].Role);
    }

    [Fact]
    public void The_name_is_bold_and_followed_by_two_spaces_then_the_phrase_in_normal_text()
    {
        var segments = AgentsPaneModel.Segments(GlyphSet.Unicode, Row(name: "worker", desc: "reads the plan"), 80);
        var name = Assert.Single(segments, s => s.Text == "worker");
        Assert.True(name.Bold);
        Assert.Equal(PaletteRole.Normal, name.Role);
        var phrase = Assert.Single(segments, s => s.Text == "  reads the plan");
        Assert.Equal(PaletteRole.Normal, phrase.Role);
        Assert.False(phrase.Bold);
    }

    [Theory]
    [InlineData(TaskRowState.Running, PaletteRole.Ok)]
    [InlineData(TaskRowState.Idle, PaletteRole.Dim)]
    [InlineData(TaskRowState.Done, PaletteRole.Dim)]
    [InlineData(TaskRowState.Failed, PaletteRole.Error)]
    [InlineData(TaskRowState.Cancelled, PaletteRole.Error)]
    public void The_bullet_takes_the_colour_of_the_tasks_state(TaskRowState state, PaletteRole role)
    {
        var segments = AgentsPaneModel.Segments(GlyphSet.Unicode, Row() with { State = state }, 80);
        Assert.Equal(role, segments[2].Role);
    }

    [Fact]
    public void A_row_with_nothing_to_report_renders_no_metrics()
    {
        // The reference's `main` line is just `● main` — no `idle` label, no numbers.
        var main = new AgentRowInfo { Name = "main", IsActive = true };
        Assert.Equal(string.Empty, AgentsPaneModel.ComposeMetrics(GlyphSet.Unicode, main));
        Assert.Equal("  ● main", AgentsPaneModel.FormatRow(GlyphSet.Unicode, main, 40, recipient: true).TrimEnd());
    }

    [Fact]
    public void Unattributable_tokens_render_a_dash_not_a_number()
    {
        var metrics = AgentsPaneModel.ComposeMetrics(GlyphSet.Unicode, Row(tokens: null));
        Assert.EndsWith("↓ —", metrics, StringComparison.Ordinal);
    }

    [Fact]
    public void A_working_row_shows_the_live_metric_pair()
    {
        Assert.Equal("3m 44s · ↓ 101.1k tokens", AgentsPaneModel.ComposeMetrics(GlyphSet.Unicode, Row()));
    }

    [Fact]
    public void The_focused_cursor_row_leads_with_the_prompt_chevron_in_the_accent()
    {
        var segments = AgentsPaneModel.Segments(GlyphSet.Unicode, Row(), 100, cursor: true);
        Assert.Equal("❯", segments[0].Text);
        Assert.Equal(PaletteRole.Accent, segments[0].Role);
        Assert.StartsWith("  ◯ ", AgentsPaneModel.FormatRow(GlyphSet.Unicode, Row(), 100), StringComparison.Ordinal);
    }

    [Fact]
    public void A_very_narrow_list_cuts_the_row_to_its_width()
    {
        Assert.Equal(10, AgentsPaneModel.FormatRow(GlyphSet.Unicode, Row(), 10).Length);
        Assert.Empty(AgentsPaneModel.Segments(GlyphSet.Unicode, Row(), 0));
    }

    [Theory]
    [InlineData(3, 6, -1, 0, 0)]
    [InlineData(10, 6, -1, 0, 0)]
    [InlineData(10, 6, 7, 0, 2)]
    [InlineData(10, 6, 1, 3, 1)]
    [InlineData(10, 6, 4, 3, 3)]
    [InlineData(10, 6, -1, 9, 4)]
    public void The_window_follows_the_cursor_and_stays_inside_the_list(int count, int visible, int cursor, int previousTop, int expectedTop)
    {
        Assert.Equal(expectedTop, AgentsPaneModel.WindowTop(count, visible, cursor, previousTop));
    }
}
