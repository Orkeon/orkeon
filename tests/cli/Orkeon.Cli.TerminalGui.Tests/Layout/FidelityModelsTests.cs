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
        foreach (var s in new[]
        {
            GlyphSet.Ascii.Prompt, GlyphSet.Ascii.Bullet, GlyphSet.Ascii.BulletHollow,
            GlyphSet.Ascii.Asterisk, GlyphSet.Ascii.Recap, GlyphSet.Ascii.Chevrons,
            GlyphSet.Ascii.RuleCell, GlyphSet.Ascii.Down, GlyphSet.Ascii.Dot,
        })
        {
            Assert.All(s, c => Assert.True(c < 128, $"non-ASCII char in fallback: '{c}'"));
        }
    }
}

public class BannerComposerTests
{
    private static BannerInfo Info(string model = "kimi-k3 (1M context) · api.moonshot.ai") => new()
    {
        ProductLine = "Orkéon Coding Agent — orkeon-repl 0.9.2",
        ModelLine = model,
        WorkspaceLine = "/workspace",
        Tips = ["Switch models anytime with /model.", "+more · /status"],
    };

    [Fact]
    public void Renders_logo_beside_the_three_lines_then_tips()
    {
        var lines = BannerComposer.Compose(Info(), GlyphSet.Unicode);
        var text = string.Join("\n", lines);
        Assert.Contains("Orkéon Coding Agent", text, StringComparison.Ordinal);
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
        // own text and may legitimately carry accents ("Orkéon") or dashes.
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
}

public class HintBarModelTests
{
    [Fact]
    public void Idle_bar_has_no_interrupt_entry()
    {
        var segments = HintBarModel.LeftSegments("default", commandRunning: false, agentsPaneAvailable: true);
        Assert.DoesNotContain(segments, s => s.Contains("interrupt", StringComparison.Ordinal));
        Assert.Contains(segments, s => s.Contains("shift+tab", StringComparison.Ordinal));
        Assert.Contains(segments, s => s.Contains("ctrl+g logs", StringComparison.Ordinal));
    }

    [Fact]
    public void Running_bar_gains_the_interrupt_entry()
    {
        // The contextuality IS the observed behaviour: capture 2's bar grows
        // "esc to interrupt" only while a turn is in flight.
        var segments = HintBarModel.LeftSegments("default", commandRunning: true, agentsPaneAvailable: true);
        Assert.Contains(segments, s => s.Contains("esc to interrupt", StringComparison.Ordinal));
    }

    [Fact]
    public void No_manage_entry_is_advertised_yet()
    {
        // The agents pane is informational (not focusable — a focusable read-only pane
        // stole the prompt focus on first live launch), so the bar must not promise a
        // "manage" action it cannot honour — with or without the pane.
        foreach (var available in new[] { true, false })
        {
            var segments = HintBarModel.LeftSegments("default", commandRunning: false, agentsPaneAvailable: available);
            Assert.DoesNotContain(segments, s => s.Contains("manage", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("bypassPermissions", true)]
    [InlineData("dontAsk", true)]
    [InlineData("acceptEdits", true)]
    [InlineData("default", false)]
    [InlineData("plan", false)]
    public void Loud_styling_marks_the_relaxed_postures_only(string mode, bool loud)
    {
        // The magenta exists to make a relaxed posture impossible to miss,
        // not to decorate the bar.
        Assert.Equal(loud, HintBarModel.IsLoudPosture(mode));
    }

    [Fact]
    public void Blank_mode_reads_default()
    {
        var segments = HintBarModel.LeftSegments("  ", commandRunning: false, agentsPaneAvailable: false);
        Assert.StartsWith("default", segments[0], StringComparison.Ordinal);
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

        using (var a = source.StartActivity("tool.call"))
            a?.SetTag("tool.name", "file_read");
        using (var a = source.StartActivity("tool.call"))
            a?.SetTag("tool.name", "shell_command");
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
        bool idle = false,
        long? tokens = 101_100)
        => new()
        {
            Name = name,
            Description = desc,
            Elapsed = TimeSpan.FromSeconds(224),
            Tokens = tokens,
            IsActive = active,
            IsIdle = idle,
        };

    [Fact]
    public void Active_row_gets_the_filled_bullet()
    {
        var line = AgentsPaneModel.FormatRow(GlyphSet.Unicode, Row(active: true), 100, 20);
        Assert.StartsWith(" ● ", line, StringComparison.Ordinal);
        var other = AgentsPaneModel.FormatRow(GlyphSet.Unicode, Row(), 100, 20);
        Assert.StartsWith(" ○ ", other, StringComparison.Ordinal);
    }

    [Fact]
    public void Metrics_are_right_aligned_and_the_prose_gives_way()
    {
        // On a narrow terminal it is the description that truncates, never the numbers.
        var line = AgentsPaneModel.FormatRow(GlyphSet.Unicode, Row(), 60, 20);
        Assert.Equal(60, line.Length);
        Assert.EndsWith("3m 44s · ↓ 101.1k tokens", line, StringComparison.Ordinal);
        Assert.Contains("…", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Idle_row_shows_idle_instead_of_metrics()
    {
        Assert.Equal("idle", AgentsPaneModel.ComposeMetrics(GlyphSet.Unicode, Row(idle: true)));
    }

    [Fact]
    public void Unattributable_tokens_render_a_dash_not_a_number()
    {
        // Two concurrent runs of the same crew cannot be told apart in the cost report
        // (PLAN TUI-G2) — a dash is honest, a number would lie.
        var metrics = AgentsPaneModel.ComposeMetrics(GlyphSet.Unicode, Row(tokens: null));
        Assert.EndsWith("↓ —", metrics, StringComparison.Ordinal);
    }

    [Fact]
    public void NameColumn_tracks_the_widest_name_with_floor_and_cap()
    {
        Assert.Equal(4, AgentsPaneModel.NameColumn([Row(name: "ab")]));
        Assert.Equal(24, AgentsPaneModel.NameColumn([Row(name: new string('x', 60))]));
    }
}
